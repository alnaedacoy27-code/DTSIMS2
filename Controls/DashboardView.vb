Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Data
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Globalization
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>A compact figure: caption, big value, and a short note (green / red / grey) under it.</summary>
Public Class DashCard
    Inherits Control

    Private Shared ReadOnly ValueFont As Font = Theme.MakeFont(Theme.HeadingFamily, 15.0F)
    Private Shared ReadOnly NoteFont As Font = Theme.MakeFont(Theme.BodyFamily, 8.5F)

    Public Property Caption As String = ""
    Public Property Value As String = "-"
    Public Property Note As String = ""
    Public Property NoteColor As Color = Theme.TextMuted
    Public Property Warn As Boolean

    Public Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or
                 ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
        Size = New Size(Theme.S(180), Theme.S(86))
    End Sub

    Public Sub SetValues(value As String, note As String, noteColor As Color, Optional warn As Boolean = False)
        Me.Value = value
        Me.Note = note
        Me.NoteColor = noteColor
        Me.Warn = warn
        Invalidate()
    End Sub

    ''' <summary>Where FigureHelp finds this card's explanation ("Dashboard"); a "?" shows when it has one.</summary>
    Public Property HelpArea As String = ""
    ''' <summary>The formula with today's / this month's real numbers, shown under the "?".</summary>
    Public Property Calculation As String = ""
    Private _hoverHelp As Boolean

    Private Function Explanation() As FigureExplanation
        Return FigureHelp.Find(HelpArea, Caption)
    End Function

    ' bottom-right, beside the short note: the captions use the whole top line
    Private ReadOnly Property BadgeTop As Integer
        Get
            Return Theme.S(54)
        End Get
    End Property

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        Dim over As Boolean = HelpBadge.Hit(Width, e.Location, BadgeTop) AndAlso Explanation() IsNot Nothing
        If over <> _hoverHelp Then
            _hoverHelp = over
            Cursor = If(over, Cursors.Hand, Cursors.Default)
            Invalidate()
        End If
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        MyBase.OnMouseLeave(e)
        If _hoverHelp Then
            _hoverHelp = False
            Cursor = Cursors.Default
            Invalidate()
        End If
    End Sub

    Protected Overrides Sub OnMouseClick(e As MouseEventArgs)
        MyBase.OnMouseClick(e)
        If e.Button <> MouseButtons.Left OrElse Not HelpBadge.Hit(Width, e.Location, BadgeTop) Then Return
        Dim x As FigureExplanation = Explanation()
        If x IsNot Nothing Then HelpBadge.Open(Me, Caption, Value & If(Note.Length > 0, "   (" & Note & ")", ""), x, BadgeTop, Calculation)
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g As Graphics = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.Clear(If(Parent IsNot Nothing, Parent.BackColor, Color.White))
        Dim r As New Rectangle(0, 0, Width - 1, Height - 1)
        Using path As GraphicsPath = Theme.RoundedRect(r, 10)
            Using b As New SolidBrush(If(Warn, Theme.AlertBack, Color.White))
                g.FillPath(b, path)
            End Using
            g.SetClip(path)
            Using accent As New SolidBrush(If(Warn, Theme.AlertRed, Theme.Maroon))
                g.FillRectangle(accent, 0, 0, Theme.S(4), Height)
            End Using
            g.ResetClip()
            Using p As New Pen(If(Warn, Color.FromArgb(240, 200, 200), Theme.BorderColor), 1.0F)
                g.DrawPath(p, path)
            End Using
        End Using
        Dim x As Integer = Theme.S(16)
        Dim w As Integer = Width - x - Theme.S(8)
        Dim hasHelp As Boolean = Explanation() IsNot Nothing
        Dim flags As TextFormatFlags = TextFormatFlags.Left Or TextFormatFlags.NoPadding Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix
        TextRenderer.DrawText(g, Caption.ToUpperInvariant(), Theme.FontCaption, New Rectangle(x, Theme.S(8), w, Theme.S(18)), Theme.TextMuted, flags)
        If hasHelp Then HelpBadge.Paint(g, Width, _hoverHelp, BadgeTop)
        TextRenderer.DrawText(g, Value, ValueFont, New Rectangle(x, Theme.S(25), w, Theme.S(30)),
                              If(Warn, Theme.AlertRed, Theme.TextDark), flags)
        TextRenderer.DrawText(g, Note, NoteFont, New Rectangle(x, Theme.S(55), w - If(hasHelp, Theme.S(22), 0), Theme.S(18)), NoteColor, flags)
    End Sub
End Class

''' <summary>
''' Home > Dashboard: today at a glance. Figures, the last 7 days of sales, the top products, recent sales,
''' low stock and who is online. Each part only shows when the person may open the matching module.
''' </summary>
Public Class DashboardView
    Inherits Panel

    ''' <summary>"Open" was clicked on a part: the module key to open.</summary>
    Public Event OpenRequested(moduleKey As String)

    Private Shared ReadOnly Green As Color = Color.FromArgb(22, 130, 60)
    Private Shared ReadOnly Inv As CultureInfo = CultureInfo.InvariantCulture

    Private ReadOnly _cards As New List(Of DashCard)()
    Private ReadOnly _chartBox As Panel = MakeBox("")
    Private ReadOnly _topBox As Panel = MakeBox("")
    Private ReadOnly _weekChart As New BarChart()
    Private ReadOnly _topChart As New BarChart()
    Private ReadOnly _lists As New List(Of Panel)()
    Private ReadOnly _message As New Label()

    Private _model As DashboardModel
    Private _loading As Boolean
    Private _again As Boolean

    ''' <summary>When the figures were read (shown next to the switch).</summary>
    Public Event Loaded(at As DateTime)
    ''' <summary>A refresh failed (database busy or offline): the figures shown are the last ones that loaded.</summary>
    Public Event LoadFailed(message As String)

    Public Sub New()
        Dock = DockStyle.Fill
        BackColor = Color.White
        AutoScroll = True
        DoubleBuffered = True

        _weekChart.Dock = DockStyle.Fill
        _topChart.Dock = DockStyle.Fill
        _chartBox.Controls.Add(_weekChart)
        _topBox.Controls.Add(_topChart)
        _chartBox.Padding = New Padding(Theme.S(6), Theme.S(4), Theme.S(10), Theme.S(6))
        _topBox.Padding = New Padding(Theme.S(6), Theme.S(4), Theme.S(10), Theme.S(6))

        _message.AutoSize = False
        _message.TextAlign = ContentAlignment.MiddleCenter
        _message.Font = Theme.FontHeading
        _message.ForeColor = Theme.TextMuted
        _message.Text = "Loading the dashboard..."
        _message.Dock = DockStyle.Fill
        Controls.Add(_message)
    End Sub

    ' ==================================================================== building blocks
    ''' <summary>A rounded box with a title (and an Open link when there is a module to go to).</summary>
    Private Shared Function MakeBox(title As String) As Panel
        Dim p As New Panel()
        p.BackColor = Color.White
        p.Tag = title
        AddHandler p.Paint,
            Sub(sender As Object, e As PaintEventArgs)
                Dim c As Panel = DirectCast(sender, Panel)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using path As GraphicsPath = Theme.RoundedRect(New Rectangle(0, 0, c.Width - 1, c.Height - 1), 10)
                    Using pen As New Pen(Theme.BorderColor, 1.0F)
                        e.Graphics.DrawPath(pen, path)
                    End Using
                End Using
            End Sub
        Return p
    End Function

    Private Function MakeList(title As String, moduleKey As String, columns As String(,), data As DataTable, emptyText As String) As Panel
        Dim box As Panel = MakeBox(title)
        box.Padding = New Padding(Theme.S(12), Theme.S(40), Theme.S(12), Theme.S(8))

        Dim t As New Label()
        t.AutoSize = True
        t.Font = Theme.MakeFont(Theme.HeadingFamily, 10.0F)
        t.ForeColor = Theme.TextDark
        t.Text = title
        t.Location = New Point(Theme.S(14), Theme.S(12))
        box.Controls.Add(t)

        If moduleKey.Length > 0 AndAlso Permissions.CanAccess(AppSession.Role, moduleKey) Then
            Dim link As New LinkLabel()
            link.AutoSize = True
            link.Text = "Open"
            link.Font = Theme.FontSmall
            link.LinkColor = Theme.Maroon
            link.ActiveLinkColor = Theme.MaroonDark
            link.LinkBehavior = LinkBehavior.HoverUnderline
            link.Anchor = AnchorStyles.Top Or AnchorStyles.Right
            link.Tag = moduleKey
            AddHandler link.LinkClicked, Sub(s, e) RaiseEvent OpenRequested(CStr(DirectCast(s, LinkLabel).Tag))
            box.Controls.Add(link)
            AddHandler box.Resize, Sub(s, e) link.Location = New Point(box.Width - link.Width - Theme.S(14), Theme.S(14))
        End If

        If data Is Nothing OrElse data.Rows.Count = 0 Then
            Dim none As New Label()
            none.Dock = DockStyle.Fill
            none.TextAlign = ContentAlignment.MiddleCenter
            none.Font = Theme.FontBase
            none.ForeColor = Theme.TextMuted
            none.Text = emptyText
            box.Controls.Add(none)
            Return box
        End If

        Dim grid As New DataGridView()
        GridStyle.Apply(grid, 27)
        grid.Dock = DockStyle.Fill
        grid.ColumnHeadersHeight = Theme.S(26)
        grid.DefaultCellStyle.Font = Theme.FontSmall
        grid.DefaultCellStyle.Padding = New Padding(Theme.S(2), 0, Theme.S(2), 0)
        grid.ColumnHeadersDefaultCellStyle.Padding = New Padding(Theme.S(2), 0, Theme.S(2), 0)
        grid.DefaultCellStyle.SelectionBackColor = Color.White
        grid.DefaultCellStyle.SelectionForeColor = Theme.TextDark
        grid.ScrollBars = ScrollBars.None
        AddHandler grid.SelectionChanged, Sub(s, e) grid.ClearSelection()
        For i As Integer = 0 To columns.GetLength(0) - 1
            Dim c As New DataGridViewTextBoxColumn()
            c.HeaderText = columns(i, 0)
            c.DataPropertyName = columns(i, 1)
            c.FillWeight = Single.Parse(columns(i, 2), Inv)
            c.SortMode = DataGridViewColumnSortMode.NotSortable
            Select Case columns(i, 3)
                Case "money"
                    c.DefaultCellStyle.Format = ReportFormat.GridFormat(ColKind.Money)
                    c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight
                    c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight
                Case "num"
                    c.DefaultCellStyle.Format = "#,##0"
                    c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight
                    c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight
                Case "time"
                    c.DefaultCellStyle.Format = "h:mm tt"
                Case "stamp"
                    c.DefaultCellStyle.Format = "MMM d, h:mm tt"
                Case "day"
                    c.DefaultCellStyle.Format = "MMM d"
            End Select
            grid.Columns.Add(c)
        Next
        grid.DataSource = data
        box.Controls.Add(grid)
        grid.BringToFront()
        Return box
    End Function

    ' ==================================================================== loading
    ''' <summary>Reads the figures again in the background (repeated calls while loading are merged).</summary>
    Public Sub RefreshData()
        If _loading Then
            _again = True
            Return
        End If
        _loading = True
        Dim role As String = AppSession.Role
        Dim uid As Integer = AppSession.UserId
        Task.Run(Function() DashboardData.Load(role, uid)).ContinueWith(
            Sub(t As Task(Of DashboardModel))
                Try
                    If IsDisposed OrElse Not IsHandleCreated Then Return
                    BeginInvoke(New Action(Sub() OnLoaded(If(t.IsFaulted, Nothing, t.Result),
                                                          If(t.IsFaulted, t.Exception.GetBaseException().Message, ""))))
                Catch ex As InvalidOperationException
                    ' the home screen was closed meanwhile
                End Try
            End Sub)
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        RefreshData()
    End Sub

    Private Sub OnLoaded(m As DashboardModel, errorText As String)
        _loading = False
        If m Is Nothing Then
            If _model Is Nothing Then
                _message.Text = "The dashboard could not be loaded: " & errorText
                _message.Visible = True
            End If
            ' do not keep old figures quietly: say so (the home screen retries)
            RaiseEvent LoadFailed(errorText)
        Else
            _model = m
            Build()
            RaiseEvent Loaded(m.LoadedAt)
        End If
        If _again Then
            _again = False
            RefreshData()
        End If
    End Sub

    ' ==================================================================== showing
    ''' <summary>"Up 12% vs yesterday", "None yesterday", "Same as last month" (short: the cards are narrow).</summary>
    Private Function Change(now As Decimal, before As Decimal, period As String) As String
        If before = 0D Then Return "None " & period
        Dim pct As Decimal = Math.Round((now - before) / before * 100D, 0)
        If pct = 0D Then Return "Same as " & period
        Return If(pct > 0D, "Up ", "Down ") & Math.Abs(pct).ToString("0", Inv) & "% vs " & period
    End Function

    Private Function ChangeColor(now As Decimal, before As Decimal) As Color
        If now > before Then Return Green
        If now < before Then Return Theme.AlertRed
        Return Theme.TextMuted
    End Function

    Private Function AddCard(caption As String) As DashCard
        Dim c As New DashCard()
        c.Caption = caption
        c.HelpArea = "Dashboard"
        _cards.Add(c)
        Controls.Add(c)
        Return c
    End Function

    Private Sub Build()
        Dim m As DashboardModel = _model
        SuspendLayout()
        Dim keepScroll As Point = AutoScrollPosition
        For Each c As Control In New List(Of Control)(_cards)
            Controls.Remove(c)
            c.Dispose()
        Next
        _cards.Clear()
        For Each p As Panel In _lists
            Controls.Remove(p)
            p.Dispose()
        Next
        _lists.Clear()
        Controls.Remove(_chartBox)
        Controls.Remove(_topBox)
        If _online IsNot Nothing Then
            Controls.Remove(_online)
            _online.Dispose()
            _online = Nothing
        End If

        Dim anything As Boolean = m.ShowSales OrElse m.ShowStock OrElse m.ShowPeople OrElse m.ShowDeliveries OrElse
                                  m.ShowGiven OrElse m.ShowCustomers
        _message.Visible = Not anything
        If Not anything Then
            _message.Text = "There is nothing for your account on the dashboard. Switch to Menu to open your modules."
            ResumeLayout()
            Return
        End If

        ' ---- figures, row 1: selling
        If m.ShowSales Then
            AddCard(If(m.OwnSalesOnly, "My sales today", "Sales today")).SetValues(
                ReportFormat.Peso(m.SalesToday), Change(m.SalesToday, m.SalesYesterday, "yesterday"), ChangeColor(m.SalesToday, m.SalesYesterday))
            Dim avg As Decimal = If(m.CountToday = 0, 0D, m.SalesToday / m.CountToday)
            AddCard("Transactions").SetValues(ReportFormat.Whole(m.CountToday),
                If(m.CountToday = 0, "No sales yet today", "Average " & ReportFormat.Peso(avg)), Theme.TextMuted)
            AddCard("Items sold today").SetValues(ReportFormat.Whole(m.ItemsToday), "pieces", Theme.TextMuted)
            AddCard(If(m.OwnSalesOnly, "My sales this month", "Sales this month")).SetValues(ReportFormat.Peso(m.SalesMonth),
                Change(m.SalesMonth, m.SalesLastMonthSoFar, "last month"), ChangeColor(m.SalesMonth, m.SalesLastMonthSoFar))
            If m.ShowCosts AndAlso Not m.OwnSalesOnly Then
                Dim margin As Decimal = If(m.SalesMonth = 0D, 0D, Math.Round(m.ProfitMonth / m.SalesMonth * 100D, 1))
                AddCard("Gross profit, month").SetValues(ReportFormat.Peso(m.ProfitMonth),
                    If(m.SalesMonth = 0D, "No sales yet", margin.ToString("0.0", Inv) & "% of sales"),
                    If(m.ProfitMonth < 0D, Theme.AlertRed, Green), m.ProfitMonth < 0D)
            End If
        End If
        If m.ShowGiven Then
            Dim share As Decimal = If(m.SalesMonth + m.GivenMonth = 0D, 0D, Math.Round(m.GivenMonth / (m.SalesMonth + m.GivenMonth) * 100D, 1))
            AddCard("Given away, month").SetValues(ReportFormat.Peso(m.GivenMonth),
                If(m.GivenMonth = 0D, "No discounts given", share.ToString("0.0", Inv) & "% of full price"), Theme.TextMuted)
        End If
        Dim firstRow As Integer = _cards.Count

        ' ---- figures, row 2: stock, deliveries, customers, people
        If m.ShowStock Then
            Dim attention As Integer = m.LowCount + m.OutCount
            AddCard("Low / out of stock").SetValues(ReportFormat.Whole(attention),
                If(attention = 0, "All products are stocked", m.LowCount & " low, " & m.OutCount & " out of stock"),
                If(attention = 0, Green, Theme.AlertRed), m.OutCount > 0)
            If m.ShowCosts Then
                AddCard("Stock value (cost)").SetValues(ReportFormat.Peso(m.StockValue), ReportFormat.Whole(m.ProductCount) & " products", Theme.TextMuted)
            End If
        End If
        If m.ShowDeliveries Then
            AddCard("Deliveries, month").SetValues(ReportFormat.Whole(m.DeliveriesMonth),
                If(m.ShowCosts, ReportFormat.Peso(m.DeliveryCostMonth) & " received", "received this month"), Theme.TextMuted)
            AddCard("Draft deliveries").SetValues(ReportFormat.Whole(m.DraftDeliveries),
                If(m.DraftDeliveries = 0, "Nothing waiting", "Not yet added to stock"),
                If(m.DraftDeliveries = 0, Green, Color.FromArgb(160, 100, 10)))
        End If
        If m.ShowCustomers Then
            AddCard("Loyalty customers").SetValues(ReportFormat.Whole(m.CustomerCount),
                ReportFormat.Whole(m.PointsOwed) & " points (" & ReportFormat.Peso(m.PointsOwed * m.PointsPesoValue) & ")", Theme.TextMuted)
        End If
        ' (who is online has its own list beside the charts; as a figure it only shows when there is room)
        If m.ShowPeople AndAlso _cards.Count - firstRow < 6 AndAlso Not m.ShowSales Then
            AddCard("Online now").SetValues(ReportFormat.Whole(m.OnlineCount), If(m.OnlineCount = 1, "person logged in", "people logged in"), Theme.TextMuted)
        End If
        _firstRow = firstRow
        AttachCalculations(m)

        ' ---- charts (and who is online beside them)
        _online = Nothing
        If m.ShowSales Then
            _weekChart.SetChart(m.Week)
            _topChart.SetChart(If(m.TopProducts.HasData, m.TopProducts, EmptyTop(m)))
            Controls.Add(_chartBox)
            Controls.Add(_topBox)
        End If
        If m.ShowPeople Then
            _online = MakeList("Online now", "user_monitoring",
                New String(,) {{"PERSON", "person", "36", ""}, {"ROLE", "role_text", "20", ""}, {"SCREEN", "screen", "44", ""}},
                m.Online, "Nobody else is logged in.")
            Controls.Add(_online)
        End If

        ' ---- lists (up to four across)
        If m.ShowSales Then
            _lists.Add(MakeList(If(m.OwnSalesOnly, "My recent sales", "Recent sales"), "sales_report",
                New String(,) {{"RECEIPT", "receipt_short", "34", ""}, {"WHEN", "when_text", "28", ""}, {"TOTAL", "total", "38", "money"}},
                m.RecentSales, "No sales yet."))
        End If
        If m.ShowStock Then
            _lists.Add(MakeList("Needs restocking", "inventory",
                New String(,) {{"PRODUCT", "product", "60", ""}, {"QTY", "qty", "20", "num"}, {"MIN", "min_qty", "20", "num"}},
                m.LowStock, "Nothing needs restocking."))
        End If
        If m.ShowDeliveries Then
            _lists.Add(MakeList("Recent deliveries", "delivery_report",
                New String(,) {{"SUPPLIER", "supplier", "38", ""}, {"DATE", "delivery_date", "24", "day"},
                               {If(m.ShowCosts, "COST", "STATUS"), If(m.ShowCosts, "total_cost", "status"), "38", If(m.ShowCosts, "money", "")}},
                m.Deliveries, "No deliveries yet."))
        End If
        If m.ShowGiven Then
            _lists.Add(MakeList("Discounts given", "loss_report",
                New String(,) {{"WHAT", "kind", "34", ""}, {"DETAIL", "detail", "34", ""}, {"AMOUNT", "amount", "32", "money"}},
                m.Losses, "No discounts given yet."))
        End If
        For Each p As Panel In _lists
            Controls.Add(p)
        Next

        LayoutParts()
        AutoScrollPosition = New Point(-keepScroll.X, -keepScroll.Y)
        ResumeLayout()
    End Sub

    Private _firstRow As Integer
    Private _online As Panel

    ''' <summary>The formula with today's / this month's numbers behind each card's "?".</summary>
    Private Sub AttachCalculations(m As DashboardModel)
        Dim setCalc As Action(Of String, String) =
            Sub(caption As String, text As String)
                For Each c As DashCard In _cards
                    If c.Caption = caption Then c.Calculation = text
                Next
            End Sub

        Dim avg As Decimal = If(m.CountToday = 0, 0D, Math.Round(m.SalesToday / m.CountToday, 2))
        Dim today As String = ReportCalc.Lines(
            "Sales today = receipt totals since midnight",
            ReportCalc.Count(m.CountToday, "receipt", "receipts") & ": " & ReportCalc.P(m.SalesToday),
            "Yesterday (whole day): " & ReportCalc.P(m.SalesYesterday) & If(m.SalesYesterday > 0D,
                ", change = (" & ReportCalc.P(m.SalesToday) & " - " & ReportCalc.P(m.SalesYesterday) & ") / " & ReportCalc.P(m.SalesYesterday) &
                " x 100 = " & ReportCalc.Pct(m.SalesToday - m.SalesYesterday, m.SalesYesterday), ""))
        setCalc("Sales today", today)
        setCalc("My sales today", today)
        setCalc("Transactions", ReportCalc.Lines(
            "Average = Sales today / Transactions",
            ReportCalc.P(m.SalesToday) & " / " & ReportCalc.N(m.CountToday) & " = " & ReportCalc.P(avg)))
        Dim month As String = ReportCalc.Lines(
            "Sales this month = receipt totals since the 1st",
            ReportCalc.P(m.SalesMonth) & " vs " & ReportCalc.P(m.SalesLastMonthSoFar) & " last month up to the same day" &
            If(m.SalesLastMonthSoFar > 0D, " = " & ReportCalc.Pct(m.SalesMonth - m.SalesLastMonthSoFar, m.SalesLastMonthSoFar) & " change", ""))
        setCalc("Sales this month", month)
        setCalc("My sales this month", month)

        Dim cost As Decimal = m.SalesMonth - m.ProfitMonth
        setCalc("Gross profit, month", ReportCalc.Lines(
            "Gross profit = sales - cost of the items sold",
            ReportCalc.P(m.SalesMonth) & " - " & ReportCalc.P(cost) & " = " & ReportCalc.P(m.ProfitMonth),
            "Margin = " & ReportCalc.P(m.ProfitMonth) & " / " & ReportCalc.P(m.SalesMonth) & " x 100 = " & ReportCalc.Pct(m.ProfitMonth, m.SalesMonth)))
        setCalc("Given away, month", ReportCalc.Lines(
            "Share = given away / (sales + given away) x 100",
            ReportCalc.P(m.GivenMonth) & " / (" & ReportCalc.P(m.SalesMonth) & " + " & ReportCalc.P(m.GivenMonth) & ") x 100 = " &
            ReportCalc.Pct(m.GivenMonth, m.SalesMonth + m.GivenMonth)))
        setCalc("Low / out of stock", ReportCalc.Lines(
            "Low / out of stock = low + out of stock",
            ReportCalc.N(m.LowCount) & " low + " & ReportCalc.N(m.OutCount) & " out = " & ReportCalc.N(m.LowCount + m.OutCount)))
        setCalc("Loyalty customers", ReportCalc.Lines(
            "Points worth = points held x value of 1 point",
            ReportCalc.N(m.PointsOwed) & " x " & ReportCalc.P(m.PointsPesoValue) & " = " & ReportCalc.P(m.PointsOwed * m.PointsPesoValue)))
    End Sub

    Private Function EmptyTop(m As DashboardModel) As ReportChart
        Dim c As New ReportChart()
        c.Title = m.TopProducts.Title & ": nothing sold yet"
        c.Horizontal = True
        Return c
    End Function

    Protected Overrides Sub OnResize(e As EventArgs)
        MyBase.OnResize(e)
        LayoutParts()
    End Sub

    ''' <summary>
    ''' Selling figures on the first row, stock / deliveries / losses / customers on the second (rows share the width evenly),
    ''' then the charts with "Online now" beside them, then up to four lists filling the rest of the height.
    ''' </summary>
    Private Sub LayoutParts()
        If _model Is Nothing Then Return
        Dim mL As Integer = Theme.S(36)
        Dim mR As Integer = Theme.S(24)
        Dim gap As Integer = Theme.S(10)
        Dim natural As Integer = ClientSize.Width - mL - mR
        Dim w As Integer = Math.Max(Theme.S(900), natural)
        Dim y As Integer = Theme.S(2)
        Dim sy As Integer = AutoScrollPosition.Y
        Dim cardH As Integer = Theme.S(76)

        ' a row holds at most 6 figures; a row with too many is split in two
        Dim rows As New List(Of List(Of DashCard))()
        Dim first As New List(Of DashCard)()
        Dim second As New List(Of DashCard)()
        For i As Integer = 0 To _cards.Count - 1
            If i < _firstRow Then first.Add(_cards(i)) Else second.Add(_cards(i))
        Next
        For Each part As List(Of DashCard) In New List(Of DashCard)() {first, second}
            If part.Count = 0 Then Continue For
            If part.Count <= 6 Then
                rows.Add(part)
            Else
                Dim half As Integer = (part.Count + 1) \ 2
                rows.Add(part.GetRange(0, half))
                rows.Add(part.GetRange(half, part.Count - half))
            End If
        Next
        For Each row As List(Of DashCard) In rows
            Dim cw As Integer = (w - gap * (row.Count - 1)) \ row.Count
            For i As Integer = 0 To row.Count - 1
                row(i).SetBounds(mL + i * (cw + gap), y + sy, cw, cardH)
            Next
            y += cardH + gap
        Next

        If _chartBox.Parent Is Me OrElse _online IsNot Nothing Then
            Dim h As Integer = Theme.S(178)
            Dim x As Integer = mL
            Dim onlineW As Integer = If(_online Is Nothing, 0, If(_chartBox.Parent Is Me, CInt(w * 0.27), w))
            Dim chartsW As Integer = w - onlineW - If(_online IsNot Nothing AndAlso _chartBox.Parent Is Me, gap, 0)
            If _chartBox.Parent Is Me Then
                Dim weekW As Integer = CInt((chartsW - gap) * 0.56)
                _chartBox.SetBounds(x, y + sy, weekW, h)
                _topBox.SetBounds(x + weekW + gap, y + sy, chartsW - weekW - gap, h)
                x += chartsW + gap
            End If
            If _online IsNot Nothing Then _online.SetBounds(x, y + sy, onlineW, h)
            y += h + gap
        End If

        If _lists.Count > 0 Then
            Dim lw As Integer = (w - gap * (_lists.Count - 1)) \ _lists.Count
            Dim lh As Integer = Math.Max(Theme.S(196), ClientSize.Height - y - Theme.S(14))
            For i As Integer = 0 To _lists.Count - 1
                _lists(i).SetBounds(mL + i * (lw + gap), y + sy, lw, lh)
            Next
            y += lh
        End If
        ' scroll sideways only when the window is narrower than the dashboard needs
        AutoScrollMinSize = New Size(If(w > natural, mL + w + mR, 0), y + Theme.S(14))
    End Sub


End Class
