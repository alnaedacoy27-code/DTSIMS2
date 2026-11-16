Option Strict On
Option Explicit On

Imports System.Data
Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' File Maintenance > Payment Methods. Cash (always there) plus e-wallets and bank accounts the shop accepts:
''' GCash, Maya, PayPal, MariBank, BPI ... with the shop's account and QR code, shown to the customer at the POS.
''' </summary>
<System.ComponentModel.DesignerCategory("Code")>
Public Class PaymentMethodsForm
    Inherits Form
    Implements ILiveForm

    Private Shared ReadOnly DetailsWidth As Integer = Theme.S(340)
    Private Shared ReadOnly ContentW As Integer = Theme.S(268)
    Private Shared ReadOnly NameFont As Font = Theme.MakeFont("Segoe UI Semibold", 10.0F)
    Private Shared ReadOnly BigNameFont As Font = Theme.MakeFont("Segoe UI Semibold", 14.0F)

    Private ReadOnly _header As New Panel()
    Private ReadOnly _title As New Label()
    Private ReadOnly _subtitle As New Label()
    Private ReadOnly _search As New SearchBox("Search payment methods...")
    Private ReadOnly _add As New MaroonButton()

    Private ReadOnly _stats As New FlowLayoutPanel()
    Private ReadOnly _cardTotal As New StatCard()
    Private ReadOnly _cardActive As New StatCard()

    Private ReadOnly _listHost As New Panel()
    Private ReadOnly _grid As New DataGridView()
    Private ReadOnly _empty As New Label()

    Private ReadOnly _details As New Panel()
    Private ReadOnly _scroll As New FlowLayoutPanel()
    Private ReadOnly _bar As New FlowLayoutPanel()
    Private ReadOnly _qr As New PictureBox()
    Private ReadOnly _dName As New Label()
    Private ReadOnly _pillRow As New Panel()
    Private ReadOnly _pill As New PillLabel()
    Private ReadOnly _vAccount As Label
    Private ReadOnly _vNumber As Label
    Private ReadOnly _vRef As Label
    Private ReadOnly _vUsed As Label
    Private ReadOnly _btnEdit As New MaroonButton()
    Private ReadOnly _btnToggle As New MaroonButton()
    Private ReadOnly _btnDelete As New MaroonButton()

    Private _loading As Boolean
    Private _selectedId As Integer
    Private _qrForId As Integer = -1

    Public Sub New()
        Me.AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi
        BackColor = Color.White
        Font = Theme.FontBase

        BuildHeader()
        BuildStats()
        BuildGrid()
        BuildDetailsTop()
        _vAccount = BuildRow("ACCOUNT NAME", 24)
        _vNumber = BuildRow("ACCOUNT / MOBILE NUMBER", 24)
        _vRef = BuildRow("REFERENCE NUMBER", 40)
        _vUsed = BuildRow("USED IN SALES", 24)
        BuildActions()

        Controls.Add(_listHost)
        Controls.Add(_details)
        Controls.Add(_stats)
        Controls.Add(_header)
    End Sub

    Public ReadOnly Property WatchedTables As String() Implements ILiveForm.WatchedTables
        Get
            Return New String() {"payment_methods", "sales"}
        End Get
    End Property

    Public Sub RefreshData() Implements ILiveForm.RefreshData
        LoadMethods()
    End Sub

    ' ------------------------------------------------------------------ building
    Private Sub BuildHeader()
        _header.Dock = DockStyle.Top
        _header.Height = Theme.S(80)
        _header.BackColor = Color.White

        _title.AutoSize = True
        _title.Font = Theme.FontTitle
        _title.ForeColor = Theme.Maroon
        _title.Text = "Payment Methods"
        _title.Location = New Point(Theme.S(32), Theme.S(10))

        _subtitle.AutoSize = True
        _subtitle.Font = Theme.FontBase
        _subtitle.ForeColor = Theme.TextMuted
        _subtitle.Text = "How customers can pay at the POS: cash, e-wallets and bank accounts, with the shop's QR codes"
        _subtitle.Location = New Point(Theme.S(35), Theme.S(50))

        _search.Size = New Size(Theme.S(260), Theme.S(40))
        AddHandler _search.SearchChanged, Sub() LoadMethods()

        _add.Text = "+  Add method"
        _add.Size = New Size(Theme.S(160), Theme.S(40))
        AddHandler _add.Click, AddressOf OnAdd

        _header.Controls.AddRange(New Control() {_title, _subtitle, _search, _add})
        AddHandler _header.Resize, Sub(s, e) PlaceHeader()
        PlaceHeader()
    End Sub

    Private Sub PlaceHeader()
        _add.Location = New Point(Math.Max(Theme.S(300), _header.ClientSize.Width - Theme.S(32) - _add.Width), Theme.S(18))
        _search.Location = New Point(Math.Max(0, _add.Left - Theme.S(16) - _search.Width), Theme.S(18))
    End Sub

    Private Sub BuildStats()
        _stats.Dock = DockStyle.Top
        _stats.Height = Theme.S(84)
        _stats.BackColor = Color.White
        _stats.FlowDirection = FlowDirection.LeftToRight
        _stats.WrapContents = False
        _stats.Padding = New Padding(Theme.S(32), Theme.S(4), 0, 0)
        For Each c As StatCard In New StatCard() {_cardTotal, _cardActive}
            c.HelpArea = "Payment Methods"
            c.Margin = New Padding(0, 0, Theme.S(14), 0)
            _stats.Controls.Add(c)
        Next
        _cardTotal.Caption = "Payment methods"
        _cardActive.Caption = "Active"
    End Sub

    Private Sub AddColumn(name As String, headerText As String, dataProp As String, weight As Single)
        Dim c As New DataGridViewTextBoxColumn()
        c.Name = name
        c.HeaderText = headerText
        c.DataPropertyName = dataProp
        c.FillWeight = weight
        c.SortMode = DataGridViewColumnSortMode.NotSortable
        _grid.Columns.Add(c)
    End Sub

    Private Sub BuildGrid()
        _listHost.Dock = DockStyle.Fill
        _listHost.BackColor = Color.White
        _listHost.Padding = New Padding(Theme.S(32), Theme.S(4), Theme.S(16), Theme.S(16))

        _grid.Dock = DockStyle.Fill
        GridStyle.Apply(_grid, 60)
        AddColumn("method", "METHOD", "name", 30)
        AddColumn("account", "ACCOUNT", "account_name", 34)
        AddColumn("reference", "REFERENCE NO.", "needs_reference", 16)
        AddColumn("status", "STATUS", "is_active", 20)
        AddHandler _grid.CellPainting, AddressOf OnCellPainting
        AddHandler _grid.SelectionChanged, Sub(s, e) If Not _loading Then ShowDetails()
        AddHandler _grid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then OnEdit(s, EventArgs.Empty)

        _empty.Dock = DockStyle.Fill
        _empty.TextAlign = ContentAlignment.MiddleCenter
        _empty.Font = Theme.FontHeading
        _empty.ForeColor = Theme.TextMuted
        _empty.BackColor = Color.White
        _empty.Visible = False

        _listHost.Controls.Add(_grid)
        _listHost.Controls.Add(_empty)
    End Sub

    Private Sub BuildDetailsTop()
        _details.Dock = DockStyle.Right
        _details.Width = DetailsWidth
        _details.BackColor = Color.FromArgb(251, 249, 249)
        DetailsLayout.Setup(_details, _scroll, _bar, New Padding(Theme.S(28), Theme.S(22), Theme.S(12), Theme.S(16)))

        _qr.Size = New Size(ContentW, Theme.S(170))
        _qr.SizeMode = PictureBoxSizeMode.Zoom
        _qr.BackColor = _details.BackColor
        _qr.Margin = New Padding(0)
        _scroll.Controls.Add(_qr)

        _dName.AutoSize = False
        _dName.Size = New Size(ContentW, Theme.S(34))
        _dName.TextAlign = ContentAlignment.MiddleCenter
        _dName.Font = BigNameFont
        _dName.ForeColor = Theme.TextDark
        _dName.UseMnemonic = False
        _dName.Margin = New Padding(0, Theme.S(6), 0, 0)
        _scroll.Controls.Add(_dName)

        _pillRow.Size = New Size(ContentW, Theme.S(32))
        _pillRow.BackColor = _details.BackColor
        _pillRow.Margin = New Padding(0, 0, 0, Theme.S(4))
        _pillRow.Controls.Add(_pill)
        _scroll.Controls.Add(_pillRow)
    End Sub

    Private Function BuildRow(caption As String, valueHeight As Integer) As Label
        Dim cap As New Label()
        cap.AutoSize = False
        cap.Size = New Size(ContentW, Theme.S(18))
        cap.Font = Theme.FontCaption
        cap.ForeColor = Theme.TextMuted
        cap.Text = caption
        cap.Margin = New Padding(0, Theme.S(10), 0, 0)
        _scroll.Controls.Add(cap)
        Dim v As New Label()
        v.AutoSize = False
        v.Size = New Size(ContentW, Theme.S(valueHeight))
        v.Font = Theme.FontBase
        v.ForeColor = Theme.TextDark
        v.UseMnemonic = False
        v.Margin = New Padding(0)
        _scroll.Controls.Add(v)
        Return v
    End Function

    Private Sub BuildActions()
        _btnEdit.Text = "Edit method"
        _btnToggle.Text = "Deactivate"
        _btnToggle.MakeSecondary()
        _btnDelete.Text = "Delete method"
        _btnDelete.MakeDanger()
        AddHandler _btnEdit.Click, AddressOf OnEdit
        AddHandler _btnToggle.Click, AddressOf OnToggle
        AddHandler _btnDelete.Click, AddressOf OnDelete
        Dim first As Boolean = True
        For Each b As MaroonButton In New MaroonButton() {_btnEdit, _btnToggle, _btnDelete}
            b.Size = New Size(ContentW, Theme.S(40))
            b.Margin = New Padding(0, If(first, 0, Theme.S(8)), 0, 0)
            first = False
            _bar.Controls.Add(b)
        Next
    End Sub

    ' ------------------------------------------------------------------ data
    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        LoadMethods()
    End Sub

    Private Sub LoadMethods()
        If _loading Then Return
        _loading = True
        Try
            Dim keep As Integer = _selectedId
            Dim dt As DataTable = PaymentRepository.GetAll(_search.Query)
            _grid.DataSource = dt
            Dim counts As DiscountCounts = PaymentRepository.GetCounts()
            _cardTotal.Value = counts.Total.ToString()
            _cardActive.Value = counts.Active.ToString()

            _empty.Visible = (dt.Rows.Count = 0)
            If dt.Rows.Count = 0 Then
                _empty.Text = If(_search.Query.Length > 0, "No payment methods match your search.", "No payment methods yet.")
                _empty.BringToFront()
            End If

            _grid.ClearSelection()
            Dim target As DataGridViewRow = Nothing
            For Each r As DataGridViewRow In _grid.Rows
                Dim drv As DataRowView = TryCast(r.DataBoundItem, DataRowView)
                If drv IsNot Nothing AndAlso Convert.ToInt32(drv("id")) = keep Then target = r
            Next
            If target Is Nothing AndAlso _grid.Rows.Count > 0 Then target = _grid.Rows(0)
            If target IsNot Nothing Then
                target.Selected = True
                _grid.CurrentCell = target.Cells(0)
            End If
        Catch ex As Exception
            _empty.Text = "Could not load the payment methods: " & ex.Message
            _empty.Visible = True
            _empty.BringToFront()
        Finally
            _loading = False
        End Try
        _qrForId = -1
        ShowDetails()
    End Sub

    Private Sub OnCellPainting(sender As Object, e As DataGridViewCellPaintingEventArgs)
        If e.RowIndex < 0 OrElse e.ColumnIndex < 0 Then Return
        Dim row As DataRowView = TryCast(_grid.Rows(e.RowIndex).DataBoundItem, DataRowView)
        If row Is Nothing Then Return
        Dim colName As String = _grid.Columns(e.ColumnIndex).Name
        e.Paint(e.CellBounds, DataGridViewPaintParts.Background Or DataGridViewPaintParts.SelectionBackground)
        Using pen As New Pen(Color.FromArgb(238, 238, 238))
            e.Graphics.DrawLine(pen, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1)
        End Using
        Dim c As Rectangle = e.CellBounds
        Dim active As Boolean = Convert.ToInt32(row("is_active")) <> 0
        Dim isCash As Boolean = row("kind").ToString() = "cash"
        Dim fore As Color = If(active, Theme.TextDark, Theme.TextMuted)
        Dim flags As TextFormatFlags = TextFormatFlags.Left Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPadding Or TextFormatFlags.NoPrefix

        Select Case colName
            Case "method"
                TextRenderer.DrawText(e.Graphics, row("name").ToString(), NameFont, New Rectangle(c.X + Theme.S(12), c.Y + Theme.S(11), c.Width - Theme.S(20), Theme.S(20)), fore, flags)
                TextRenderer.DrawText(e.Graphics, PaymentRepository.KindText(row("kind").ToString()) & If(Convert.ToInt32(row("has_qr")) <> 0, "  |  QR code", ""),
                                      Theme.FontSmall, New Rectangle(c.X + Theme.S(12), c.Y + Theme.S(33), c.Width - Theme.S(20), Theme.S(18)), Theme.TextMuted, flags)
            Case "account"
                Dim acc As String = row("account_name").ToString()
                Dim num As String = row("account_number").ToString()
                Dim line1 As String = If(isCash, "Paid at the counter", If(acc.Length > 0, acc, "No account name"))
                TextRenderer.DrawText(e.Graphics, line1, Theme.FontBase, New Rectangle(c.X + Theme.S(12), c.Y + Theme.S(11), c.Width - Theme.S(20), Theme.S(20)), fore, flags)
                If num.Length > 0 Then
                    TextRenderer.DrawText(e.Graphics, PaymentRepository.Masked(num), Theme.FontSmall,
                                          New Rectangle(c.X + Theme.S(12), c.Y + Theme.S(33), c.Width - Theme.S(20), Theme.S(18)), Theme.TextMuted, flags)
                End If
            Case "reference"
                Dim t As String = If(isCash, "-", If(Convert.ToInt32(row("needs_reference")) <> 0, "Required", "Optional"))
                TextRenderer.DrawText(e.Graphics, t, Theme.FontBase, New Rectangle(c.X + Theme.S(12), c.Y, c.Width - Theme.S(16), c.Height), fore,
                                      flags Or TextFormatFlags.VerticalCenter)
            Case "status"
                Dim t As String = If(active, "Active", "Inactive")
                Pills.Draw(e.Graphics, c, t, Pills.StatusBack(t), Pills.StatusFore(t))
        End Select
        e.Handled = True
    End Sub

    Private Function SelectedRow() As DataRowView
        If _grid.SelectedRows.Count = 0 Then Return Nothing
        Return TryCast(_grid.SelectedRows(0).DataBoundItem, DataRowView)
    End Function

    Private Sub ShowDetails()
        Dim row As DataRowView = SelectedRow()
        If row Is Nothing Then
            _selectedId = 0
            _details.Visible = False
            Return
        End If
        _details.Visible = True
        _selectedId = Convert.ToInt32(row("id"))
        Dim isCash As Boolean = row("kind").ToString() = "cash"
        Dim active As Boolean = Convert.ToInt32(row("is_active")) <> 0

        ' the QR is only read when another method is selected
        If _qrForId <> _selectedId Then
            _qrForId = _selectedId
            Dim old As Image = _qr.Image
            _qr.Image = Nothing
            If old IsNot Nothing Then old.Dispose()
            If Convert.ToInt32(row("has_qr")) <> 0 Then
                Try
                    _qr.Image = PaymentRepository.ToImage(PaymentRepository.GetQr(_selectedId))
                Catch
                    _qr.Image = Nothing
                End Try
            End If
        End If
        _qr.Visible = (_qr.Image IsNot Nothing)

        _dName.Text = row("name").ToString()
        Dim t As String = If(active, "Active", "Inactive")
        _pill.SetPill(t, Pills.StatusBack(t), Pills.StatusFore(t))
        _pill.Location = New Point(Math.Max(0, (_pillRow.Width - _pill.Width) \ 2), Theme.S(4))

        Dim acc As String = row("account_name").ToString()
        Dim num As String = row("account_number").ToString()
        _vAccount.Text = If(isCash, "-", If(acc.Length > 0, acc, "Not set"))
        _vNumber.Text = If(isCash, "-", If(num.Length > 0, num, "Not set"))
        _vRef.Text = If(isCash, "Not needed for cash",
                        If(Convert.ToInt32(row("needs_reference")) <> 0, "Required: the cashier types it from the customer's confirmation", "Optional"))
        Dim used As Integer = Convert.ToInt32(row("used_count"))
        _vUsed.Text = used.ToString() & If(used = 1, " sale", " sales")

        _btnEdit.Enabled = Not isCash
        _btnToggle.Enabled = Not isCash
        _btnDelete.Enabled = Not isCash
        _btnToggle.Text = If(active, "Deactivate", "Activate")
    End Sub

    ' ------------------------------------------------------------------ actions
    Private Sub OnAdd(sender As Object, e As EventArgs)
        Using dlg As New PaymentMethodEditForm()
            If dlg.ShowDialog(OwnerOf(Me)) = DialogResult.OK Then LoadMethods()
        End Using
    End Sub

    Private Sub OnEdit(sender As Object, e As EventArgs)
        Dim row As DataRowView = SelectedRow()
        If row Is Nothing OrElse row("kind").ToString() = "cash" Then Return
        Using dlg As New PaymentMethodEditForm(_selectedId)
            If dlg.ShowDialog(OwnerOf(Me)) = DialogResult.OK Then LoadMethods()
        End Using
    End Sub

    Private Sub OnToggle(sender As Object, e As EventArgs)
        Dim row As DataRowView = SelectedRow()
        If row Is Nothing OrElse row("kind").ToString() = "cash" Then Return
        Dim name As String = row("name").ToString()
        Dim wasActive As Boolean = Convert.ToInt32(row("is_active")) <> 0
        Try
            If PaymentRepository.SetActive(_selectedId, Not wasActive) = SaveResult.Ok Then
                AuditLog.Write(If(wasActive, "DEACTIVATE", "ACTIVATE"), "Payment Methods",
                               If(wasActive, "Deactivated", "Activated") & " payment method '" & name & "'")
            End If
        Catch ex As Exception
            Notify(Me, "Payment Methods", "Could not change the payment method: " & ex.Message, MessageBoxIcon.Error)
        End Try
        LoadMethods()
    End Sub

    Private Sub OnDelete(sender As Object, e As EventArgs)
        Dim row As DataRowView = SelectedRow()
        If row Is Nothing OrElse row("kind").ToString() = "cash" Then Return
        Dim name As String = row("name").ToString()
        If Not Confirm(Me, "Payment Methods", "Delete the payment method '" & name & "'?" & Environment.NewLine & Environment.NewLine &
                       "Past sales keep the method's name and their reference numbers." & Environment.NewLine &
                       "If you only want to hide it at the POS, use Deactivate instead.", MessageBoxIcon.Warning) Then Return
        Try
            If PaymentRepository.Delete(_selectedId) = SaveResult.Ok Then
                AuditLog.Write("DELETE", "Payment Methods", "Deleted payment method '" & name & "'")
            End If
        Catch ex As Exception
            Notify(Me, "Payment Methods", "Could not delete the payment method: " & ex.Message, MessageBoxIcon.Error)
        End Try
        _selectedId = 0
        LoadMethods()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing AndAlso _qr.Image IsNot Nothing Then _qr.Image.Dispose()
        MyBase.Dispose(disposing)
    End Sub

End Class
