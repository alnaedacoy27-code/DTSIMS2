Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Reflection
Imports System.Windows.Forms

' =====================================================================================
' UiKit: small reusable pieces shared by every module screen
'   Pills, PillLabel, Avatar, StatCard, SearchBox, GridStyle
' =====================================================================================

''' <summary>Rounded "pill" labels (role, status ...) and their colors.</summary>
Public Module Pills

    Private ReadOnly PillFont As Font = Theme.MakeFont("Segoe UI Semibold", 8.5F)

    Public Function Measure(text As String) As Integer
        Return TextRenderer.MeasureText(text, PillFont, New Size(2000, 100), TextFormatFlags.NoPadding).Width + 24
    End Function

    ''' <summary>Draws a pill, left-aligned and vertically centered inside a grid cell.</summary>
    Public Sub Draw(g As Graphics, cell As Rectangle, text As String, back As Color, fore As Color)
        Dim r As New Rectangle(cell.X + Theme.S(10), cell.Y + (cell.Height - Theme.S(24)) \ 2, Measure(text), Theme.S(24))
        PaintPill(g, r, text, back, fore)
    End Sub

    Public Sub PaintPill(g As Graphics, r As Rectangle, text As String, back As Color, fore As Color)
        g.SmoothingMode = SmoothingMode.AntiAlias
        Using path As GraphicsPath = Theme.RoundedRect(r, r.Height \ 2)
            Using b As New SolidBrush(back)
                g.FillPath(b, path)
            End Using
        End Using
        TextRenderer.DrawText(g, text, PillFont, r, fore,
                              TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding)
    End Sub

    Public Function RoleBack(role As String) As Color
        Select Case Permissions.NormalizeRole(role)
            Case "superadmin"
                Return Color.FromArgb(249, 228, 228)
            Case "admin"
                Return Color.FromArgb(228, 238, 252)
            Case Else
                Return Color.FromArgb(238, 238, 238)
        End Select
    End Function

    Public Function RoleFore(role As String) As Color
        Select Case Permissions.NormalizeRole(role)
            Case "superadmin"
                Return Theme.Maroon
            Case "admin"
                Return Color.FromArgb(28, 88, 178)
            Case Else
                Return Color.FromArgb(90, 90, 90)
        End Select
    End Function

    Public Function StatusBack(status As String) As Color
        Select Case status
            Case "Active"
                Return Color.FromArgb(226, 246, 234)
            Case "Locked"
                Return Color.FromArgb(253, 230, 230)
            Case "Disabled"
                Return Color.FromArgb(232, 232, 232)
            Case "Inactive"
                Return Color.FromArgb(235, 239, 245)
            Case Else
                Return Color.FromArgb(255, 243, 219)
        End Select
    End Function

    Public Function StatusFore(status As String) As Color
        Select Case status
            Case "Active"
                Return Color.FromArgb(22, 130, 60)
            Case "Locked"
                Return Theme.AlertRed
            Case "Disabled"
                Return Color.FromArgb(100, 100, 100)
            Case "Inactive"
                Return Color.FromArgb(95, 110, 130)
            Case Else
                Return Color.FromArgb(160, 100, 10)
        End Select
    End Function

End Module

''' <summary>One pill as a control (used in detail panels).</summary>
Public Class PillLabel
    Inherits Control

    Private _text As String = ""
    Private _back As Color = Color.Gainsboro
    Private _fore As Color = Color.DimGray

    Public Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or
                 ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
        Size = New Size(Theme.S(80), Theme.S(24))
    End Sub

    Public Sub SetPill(text As String, back As Color, fore As Color)
        _text = text
        _back = back
        _fore = fore
        Size = New Size(Pills.Measure(text), Theme.S(24))
        Invalidate()
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        e.Graphics.Clear(If(Parent IsNot Nothing, Parent.BackColor, Color.White))
        Pills.PaintPill(e.Graphics, New Rectangle(0, 0, Width - 1, Height - 1), _text, _back, _fore)
    End Sub
End Class

''' <summary>Round photo, or maroon initials when there is no photo.</summary>
Public Class Avatar
    Inherits Control

    Private _photo As Image
    Private _name As String = ""

    Public Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or
                 ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
        Size = New Size(Theme.S(96), Theme.S(96))
    End Sub

    Public Property DisplayName As String
        Get
            Return _name
        End Get
        Set(v As String)
            _name = If(v, "")
            Invalidate()
        End Set
    End Property

    ''' <summary>The avatar owns the image and disposes the previous one when it changes.</summary>
    Public Property Photo As Image
        Get
            Return _photo
        End Get
        Set(v As Image)
            If _photo IsNot Nothing AndAlso Not Object.ReferenceEquals(_photo, v) Then _photo.Dispose()
            _photo = v
            Invalidate()
        End Set
    End Property

    Public Shared Function Initials(name As String) As String
        Dim parts As String() = If(name, "").Trim().Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
        If parts.Length = 0 Then Return "?"
        If parts.Length = 1 Then Return parts(0).Substring(0, 1).ToUpperInvariant()
        Return (parts(0).Substring(0, 1) & parts(parts.Length - 1).Substring(0, 1)).ToUpperInvariant()
    End Function

    ''' <summary>Used by grids too: a maroon-tinted circle with initials.</summary>
    Public Shared Sub PaintInitials(g As Graphics, bounds As Rectangle, name As String, fontSize As Single)
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim r As New Rectangle(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1)
        Using b As New SolidBrush(Theme.MaroonTint)
            g.FillEllipse(b, r)
        End Using
        Using p As New Pen(Color.FromArgb(240, 214, 214), 1.0F)
            g.DrawEllipse(p, r)
        End Using
        Using f As New Font("Segoe UI Semibold", fontSize)
            TextRenderer.DrawText(g, Initials(name), f, bounds, Theme.Maroon,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding)
        End Using
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g As Graphics = e.Graphics
        g.Clear(If(Parent IsNot Nothing, Parent.BackColor, Color.White))
        g.SmoothingMode = SmoothingMode.AntiAlias

        If _photo Is Nothing Then
            PaintInitials(g, New Rectangle(0, 0, Width, Height), _name, Height * 0.26F)
            Return
        End If

        Dim r As New Rectangle(0, 0, Width - 1, Height - 1)
        Dim side As Integer = Math.Min(_photo.Width, _photo.Height)
        Dim src As New Rectangle((_photo.Width - side) \ 2, (_photo.Height - side) \ 2, side, side)
        Using path As New GraphicsPath()
            path.AddEllipse(r)
            g.SetClip(path)
            g.InterpolationMode = InterpolationMode.HighQualityBicubic
            g.DrawImage(_photo, r, src, GraphicsUnit.Pixel)
            g.ResetClip()
        End Using
        Using p As New Pen(Color.FromArgb(235, 220, 220), 1.0F)
            g.DrawEllipse(p, r)
        End Using
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing AndAlso _photo IsNot Nothing Then
            _photo.Dispose()
            _photo = Nothing
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class

''' <summary>A small number-and-label card, e.g. "Admin 2 / 5".</summary>
Public Class StatCard
    Inherits Control

    Private Shared ReadOnly ValueFont As Font = Theme.MakeFont("Segoe UI Semibold", 16.0F)

    Private _value As String = "0"
    Private _caption As String = ""
    Private _warn As Boolean

    Public Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or
                 ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
        Size = New Size(Theme.S(200), Theme.S(72))
    End Sub

    Public Property Value As String
        Get
            Return _value
        End Get
        Set(v As String)
            _value = v
            Invalidate()
        End Set
    End Property

    Public Property Caption As String
        Get
            Return _caption
        End Get
        Set(v As String)
            _caption = v
            Invalidate()
        End Set
    End Property

    ''' <summary>Shows the number in red (limit reached, locked accounts ...).</summary>
    Public Property Warn As Boolean
        Get
            Return _warn
        End Get
        Set(v As Boolean)
            _warn = v
            Invalidate()
        End Set
    End Property

    Private _helpArea As String = ""
    Private _hoverHelp As Boolean

    ''' <summary>The formula with the real numbers behind this figure (reports fill it in); shown under the "?".</summary>
    Public Property Calculation As String = ""

    ''' <summary>
    ''' The screen or report this card is on (e.g. "Sales Report", "Users"). When FigureHelp has an explanation
    ''' for this area and caption, a small "?" shows in the corner; clicking it explains the number.
    ''' </summary>
    Public Property HelpArea As String
        Get
            Return _helpArea
        End Get
        Set(v As String)
            _helpArea = If(v, "")
            Invalidate()
        End Set
    End Property

    Private Function Explanation() As FigureExplanation
        Return FigureHelp.Find(_helpArea, _caption)
    End Function

    ''' <summary>Where the "?" is drawn (and can be clicked).</summary>
    Private Function HelpRect() As Rectangle
        Dim d As Integer = Theme.S(18)
        Return New Rectangle(Width - d - Theme.S(9), Theme.S(8), d, d)
    End Function

    Private Function OverHelp(p As Point) As Boolean
        Dim r As Rectangle = HelpRect()
        r.Inflate(Theme.S(5), Theme.S(5))
        Return r.Contains(p) AndAlso Explanation() IsNot Nothing
    End Function

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        Dim over As Boolean = OverHelp(e.Location)
        If over <> _hoverHelp Then
            _hoverHelp = over
            Cursor = If(over, Cursors.Hand, Cursors.Default)
            Invalidate(HelpRect())
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
        If e.Button <> MouseButtons.Left OrElse Not OverHelp(e.Location) Then Return
        Dim x As FigureExplanation = Explanation()
        If x Is Nothing Then Return
        Dim r As Rectangle = HelpRect()
        FigureHelpPopup.ShowFor(Me, New Point(r.Right, r.Bottom + Theme.S(4)), _caption, _value, x, Calculation)
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g As Graphics = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.Clear(If(Parent IsNot Nothing, Parent.BackColor, Color.White))

        Dim r As New Rectangle(0, 0, Width - 1, Height - 1)
        Using path As GraphicsPath = Theme.RoundedRect(r, 10)
            Using b As New SolidBrush(Color.White)
                g.FillPath(b, path)
            End Using
            Using p As New Pen(Theme.BorderColor, 1.0F)
                g.DrawPath(p, path)
            End Using
        End Using

        Dim hasHelp As Boolean = Explanation() IsNot Nothing
        Dim textW As Integer = Width - 24 - If(hasHelp, Theme.S(24), 0)
        TextRenderer.DrawText(g, _value, ValueFont, New Rectangle(16, 8, textW, 36),
                              If(_warn, Theme.AlertRed, Theme.Maroon),
                              TextFormatFlags.Left Or TextFormatFlags.NoPadding Or TextFormatFlags.EndEllipsis)
        TextRenderer.DrawText(g, _caption, Theme.FontSmall, New Rectangle(16, 44, Width - 24, 22),
                              Theme.TextMuted, TextFormatFlags.Left Or TextFormatFlags.NoPadding Or TextFormatFlags.NoPrefix)

        If hasHelp Then
            Dim h As Rectangle = HelpRect()
            Dim accent As Color = If(_hoverHelp, Theme.Maroon, Color.FromArgb(170, 170, 170))
            If _hoverHelp Then
                Using b As New SolidBrush(Theme.MaroonTint)
                    g.FillEllipse(b, h)
                End Using
            End If
            Using p As New Pen(accent, 1.3F)
                g.DrawEllipse(p, h)
            End Using
            TextRenderer.DrawText(g, "?", Theme.FontCaption, h, accent,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding)
        End If
    End Sub
End Class

''' <summary>The small "?" in the top-right corner of a figure tile (StatCard, DashCard): where it is, drawing it, clicking it.</summary>
Public Module HelpBadge

    ''' <param name="top">-1 = the top-right corner; otherwise the y of the circle (DashCard puts it beside its note).</param>
    Public Function RectFor(tileWidth As Integer, Optional top As Integer = -1) As Rectangle
        Dim d As Integer = Theme.S(18)
        Return New Rectangle(tileWidth - d - Theme.S(9), If(top < 0, Theme.S(8), top), d, d)
    End Function

    ''' <summary>A slightly bigger area than the circle, so it is easy to hit.</summary>
    Public Function Hit(tileWidth As Integer, p As Point, Optional top As Integer = -1) As Boolean
        Dim r As Rectangle = RectFor(tileWidth, top)
        r.Inflate(Theme.S(5), Theme.S(5))
        Return r.Contains(p)
    End Function

    Public Sub Paint(g As Graphics, tileWidth As Integer, hover As Boolean, Optional top As Integer = -1)
        Dim h As Rectangle = RectFor(tileWidth, top)
        Dim accent As Color = If(hover, Theme.Maroon, Color.FromArgb(170, 170, 170))
        g.SmoothingMode = SmoothingMode.AntiAlias
        If hover Then
            Using b As New SolidBrush(Theme.MaroonTint)
                g.FillEllipse(b, h)
            End Using
        End If
        Using p As New Pen(accent, 1.3F)
            g.DrawEllipse(p, h)
        End Using
        TextRenderer.DrawText(g, "?", Theme.FontCaption, h, accent,
                              TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding)
    End Sub

    ''' <summary>Opens the explanation under the "?" of this tile.</summary>
    Public Sub Open(tile As Control, caption As String, value As String, x As FigureExplanation, Optional top As Integer = -1,
                    Optional calculation As String = "")
        Dim r As Rectangle = RectFor(tile.Width, top)
        FigureHelpPopup.ShowFor(tile, New Point(r.Right, r.Bottom + Theme.S(4)), caption, value, x, calculation)
    End Sub

End Module

''' <summary>The little window a "?" on a figure opens: what the number means, how it is worked out, where it comes from.</summary>
Public Module FigureHelpPopup

    Private _open As ToolStripDropDown

    Public Sub ShowFor(card As Control, anchorTopRight As Point, caption As String, value As String, x As FigureExplanation,
                       Optional calculation As String = "")
        If _open IsNot Nothing Then
            _open.Close()
            _open = Nothing
        End If

        Dim panel As Panel = BuildPanel(caption, value, x, calculation)
        Dim w As Integer = panel.Width

        Dim host As New ToolStripControlHost(panel)
        host.Margin = Padding.Empty
        host.Padding = Padding.Empty
        host.AutoSize = False
        host.Size = panel.Size

        Dim dd As New ToolStripDropDown()
        dd.Padding = Padding.Empty
        dd.Margin = Padding.Empty
        dd.DropShadowEnabled = True
        dd.Items.Add(host)
        AddHandler dd.Closed, Sub(s, e) If ReferenceEquals(_open, dd) Then _open = Nothing
        _open = dd

        ' right edge under the "?", kept on screen
        Dim screenPt As Point = card.PointToScreen(anchorTopRight)
        Dim area As Rectangle = Screen.FromControl(card).WorkingArea
        Dim left As Integer = Math.Max(area.Left + 4, Math.Min(screenPt.X - w, area.Right - w - 4))
        Dim top As Integer = screenPt.Y
        If top + panel.Height > area.Bottom - 4 Then top = Math.Max(area.Top + 4, card.PointToScreen(Point.Empty).Y - panel.Height - 4)
        dd.Show(New Point(left, top))
    End Sub

    ''' <summary>The contents of the popup: title, the number shown now, how it is calculated, where it comes from.</summary>
    Public Function BuildPanel(caption As String, value As String, x As FigureExplanation, Optional calculation As String = "") As Panel
        Dim hasCalc As Boolean = Not String.IsNullOrWhiteSpace(calculation)
        Dim w As Integer = Theme.S(If(hasCalc, 470, 380))
        Dim pad As Integer = Theme.S(16)
        Dim panel As New Panel()
        panel.BackColor = Color.White
        panel.Width = w

        Dim y As Integer = pad
        Dim title As Label = MakeLabel(caption, Theme.FontHeading, Theme.Maroon, w - 2 * pad)
        title.Location = New Point(pad, y)
        panel.Controls.Add(title)
        y = title.Bottom + Theme.S(2)

        If Not String.IsNullOrEmpty(value) Then
            Dim v As Label = MakeLabel("Now showing: " & value, Theme.FontSmall, Theme.TextMuted, w - 2 * pad)
            v.Location = New Point(pad, y)
            panel.Controls.Add(v)
            y = v.Bottom + Theme.S(10)
        End If

        If hasCalc Then
            ' the formula (first line) in bold, then the same formula with this period's numbers
            Dim calcText As String = calculation.Replace(vbCrLf, vbLf)
            Dim nl As Integer = calcText.IndexOf(vbLf, StringComparison.Ordinal)
            Dim formula As String = If(nl < 0, calcText, calcText.Substring(0, nl))
            Dim rest As String = If(nl < 0, "", calcText.Substring(nl + 1))
            Dim h As Label = MakeLabel("THE CALCULATION, WITH THESE NUMBERS", Theme.FontCaption, Theme.TextMuted, w - 2 * pad)
            h.Location = New Point(pad, y)
            panel.Controls.Add(h)
            Dim f As Label = MakeLabel(formula, Theme.MakeFont(Theme.HeadingFamily, 10.0F), Theme.Maroon, w - 2 * pad)
            f.Location = New Point(pad, h.Bottom + Theme.S(3))
            panel.Controls.Add(f)
            y = f.Bottom
            If rest.Length > 0 Then
                Dim b As Label = MakeLabel(rest.Replace(vbLf, Environment.NewLine), Theme.FontBase, Theme.TextDark, w - 2 * pad)
                b.Location = New Point(pad, y + Theme.S(3))
                panel.Controls.Add(b)
                y = b.Bottom
            End If
            y = AddSection(panel, "ABOUT THIS NUMBER", x.How, pad, y + Theme.S(10), w, Theme.FontSmall)
        Else
            y = AddSection(panel, "HOW IT IS CALCULATED", x.How, pad, y, w)
        End If
        If Not String.IsNullOrEmpty(x.Source) Then y = AddSection(panel, "WHERE THE NUMBERS COME FROM", x.Source, pad, y + Theme.S(8), w,
                                                                   If(hasCalc, Theme.FontSmall, Theme.FontBase))
        panel.Height = y + pad
        AddHandler panel.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Using pen As New Pen(Theme.Maroon, 2.0F)
                    e.Graphics.DrawLine(pen, 0, 1, panel.Width, 1)
                End Using
                Using pen As New Pen(Theme.BorderColor, 1.0F)
                    e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1)
                End Using
            End Sub
        Return panel
    End Function

    Private Function AddSection(panel As Panel, heading As String, body As String, pad As Integer, y As Integer, w As Integer,
                                Optional bodyFont As Font = Nothing) As Integer
        Dim h As Label = MakeLabel(heading, Theme.FontCaption, Theme.TextMuted, w - 2 * pad)
        h.Location = New Point(pad, y)
        panel.Controls.Add(h)
        Dim b As Label = MakeLabel(body, If(bodyFont, Theme.FontBase), If(bodyFont Is Nothing, Theme.TextDark, Theme.TextMuted), w - 2 * pad)
        b.Location = New Point(pad, h.Bottom + Theme.S(3))
        panel.Controls.Add(b)
        Return b.Bottom
    End Function

    Private Function MakeLabel(text As String, f As Font, c As Color, maxW As Integer) As Label
        Dim l As New Label()
        l.AutoSize = True
        l.MaximumSize = New Size(maxW, 0)
        l.UseMnemonic = False
        l.Font = f
        l.ForeColor = c
        l.BackColor = Color.White
        l.Text = text
        Return l
    End Function

End Module

''' <summary>Rounded search box with a magnifier and placeholder text. Raises SearchChanged after typing pauses.</summary>
Public Class SearchBox
    Inherits Panel

    Private Declare Auto Function SendMessage Lib "user32.dll" (hWnd As IntPtr, msg As Integer, wParam As IntPtr, lParam As String) As IntPtr
    Private Const EM_SETCUEBANNER As Integer = &H1501

    Public Event SearchChanged()

    Private ReadOnly _text As New TextBox()
    Private ReadOnly _timer As New Timer()
    Private ReadOnly _cue As String
    Private _focused As Boolean

    Public Sub New(placeholder As String)
        _cue = placeholder
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or
                 ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
        Size = New Size(Theme.S(280), Theme.S(40))
        BackColor = Color.White

        _text.BorderStyle = BorderStyle.None
        _text.Font = Theme.FontBase
        _text.BackColor = Color.White
        _text.ForeColor = Theme.TextDark
        Controls.Add(_text)
        LayoutText()

        AddHandler _text.HandleCreated, AddressOf OnTextHandleCreated
        AddHandler _text.TextChanged, AddressOf OnInnerTextChanged
        AddHandler _text.Enter, AddressOf OnTextEnter
        AddHandler _text.Leave, AddressOf OnTextLeave

        _timer.Interval = 300
        AddHandler _timer.Tick, AddressOf OnTimerTick
    End Sub

    ''' <summary>What the user typed (trimmed).</summary>
    Public ReadOnly Property Query As String
        Get
            Return _text.Text.Trim()
        End Get
    End Property

    Private Sub LayoutText()
        _text.Left = Theme.S(38)
        _text.Width = Math.Max(Theme.S(20), Width - Theme.S(38) - Theme.S(14))
        _text.Top = (Height - _text.Height) \ 2
    End Sub

    Protected Overrides Sub OnResize(eventargs As EventArgs)
        MyBase.OnResize(eventargs)
        LayoutText()
    End Sub

    Private Sub OnTextHandleCreated(sender As Object, e As EventArgs)
        SendMessage(_text.Handle, EM_SETCUEBANNER, New IntPtr(1), _cue)
    End Sub

    Private Sub OnInnerTextChanged(sender As Object, e As EventArgs)
        _timer.Stop()
        _timer.Start()
    End Sub

    Private Sub OnTimerTick(sender As Object, e As EventArgs)
        _timer.Stop()
        RaiseEvent SearchChanged()
    End Sub

    Private Sub OnTextEnter(sender As Object, e As EventArgs)
        _focused = True
        Invalidate()
    End Sub

    Private Sub OnTextLeave(sender As Object, e As EventArgs)
        _focused = False
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        _text.Focus()
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g As Graphics = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.Clear(If(Parent IsNot Nothing, Parent.BackColor, Color.White))

        Dim r As New Rectangle(0, 0, Width - 1, Height - 1)
        Using path As GraphicsPath = Theme.RoundedRect(r, 8)
            Using b As New SolidBrush(Color.White)
                g.FillPath(b, path)
            End Using
            Using p As New Pen(If(_focused, Theme.Maroon, Theme.BorderColor), If(_focused, 1.8F, 1.0F))
                g.DrawPath(p, path)
            End Using
        End Using

        Dim cy As Integer = Height \ 2
        Using p As New Pen(Theme.TextMuted, 1.8F)
            g.DrawEllipse(p, 13, cy - 8, 11, 11)
            g.DrawLine(p, Theme.S(22), cy + 1, Theme.S(27), cy + Theme.S(6))
        End Using
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _timer.Stop()
            _timer.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class

''' <summary>One modern, minimal look for every list in the system.</summary>
Public Module GridStyle

    Public Sub Apply(grid As DataGridView, Optional rowHeight As Integer = 60)
        grid.AutoGenerateColumns = False
        grid.AllowUserToAddRows = False
        grid.AllowUserToDeleteRows = False
        grid.AllowUserToResizeRows = False
        grid.AllowUserToResizeColumns = False
        grid.AllowUserToOrderColumns = False
        grid.ReadOnly = True
        grid.MultiSelect = False
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        grid.RowHeadersVisible = False
        grid.BorderStyle = BorderStyle.None
        grid.BackgroundColor = Color.White
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal
        grid.GridColor = Color.FromArgb(238, 238, 238)
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        grid.EnableHeadersVisualStyles = False
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
        grid.ColumnHeadersHeight = Theme.S(40)

        With grid.ColumnHeadersDefaultCellStyle
            .BackColor = Color.White
            .ForeColor = Theme.TextMuted
            .Font = Theme.FontCaption
            .SelectionBackColor = Color.White
            .SelectionForeColor = Theme.TextMuted
            .Alignment = DataGridViewContentAlignment.MiddleLeft
            .Padding = New Padding(Theme.S(10), 0, 0, 0)
        End With

        With grid.DefaultCellStyle
            .BackColor = Color.White
            .ForeColor = Theme.TextDark
            .Font = Theme.FontBase
            .SelectionBackColor = Theme.MaroonTint
            .SelectionForeColor = Theme.TextDark
            .Padding = New Padding(Theme.S(10), 0, 0, 0)
        End With

        grid.RowTemplate.Height = Theme.S(rowHeight)

        ' smoother scrolling
        Dim pi As PropertyInfo = GetType(DataGridView).GetProperty("DoubleBuffered", BindingFlags.Instance Or BindingFlags.NonPublic)
        If pi IsNot Nothing Then pi.SetValue(grid, True, Nothing)
    End Sub

End Module
