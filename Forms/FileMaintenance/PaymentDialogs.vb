Option Strict On
Option Explicit On

Imports System.Data
Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

''' <summary>Add or edit a payment method: name, type, the shop's account, whether a reference number is asked, and its QR code.</summary>
Public Class PaymentMethodEditForm
    Inherits ModalDialog

    Private ReadOnly _id As Integer
    Private ReadOnly _name As New InputField("Name, e.g. GCash, Maya, BPI", InputKind.Title, Theme.S(300), True)
    Private ReadOnly _kind As New ChoiceField("Type", Theme.S(200))
    Private ReadOnly _accName As New InputField("Account name (as the customer sees it)", InputKind.Title, Theme.S(300), False)
    Private ReadOnly _accNumber As New InputField("Account / mobile number", InputKind.Text, Theme.S(200), False)
    Private ReadOnly _needsRef As New CheckBox()
    Private ReadOnly _refKind As New ChoiceField("Reference number has", Theme.S(200))
    Private ReadOnly _refMin As New InputField("Shortest", InputKind.WholeNumber, Theme.S(100), True)
    Private ReadOnly _refMax As New InputField("Longest", InputKind.WholeNumber, Theme.S(100), True)
    Private ReadOnly _suggest As New LinkLabel()
    Private ReadOnly _qr As New PictureBox()
    Private ReadOnly _qrNote As New Label()
    Private ReadOnly _choose As New LinkLabel()
    Private ReadOnly _remove As New LinkLabel()
    Private ReadOnly _save As New MaroonButton()
    Private ReadOnly _cancel As New MaroonButton()

    Private Const NumbersOnly As String = "Numbers only"
    Private Const LettersAndNumbers As String = "Letters and numbers"

    Private _qrBytes As Byte()
    Private _qrChanged As Boolean
    Private _qrCleared As Boolean
    Private _ruleTouched As Boolean
    Private _settingRule As Boolean

    Public Sub New()
        MyBase.New("Add payment method", "A way customers can pay besides cash, with the shop's account and QR code.", Theme.S(760), Theme.S(560))
        _id = 0
        Build()
        _kind.Value = "E-wallet"
        _needsRef.Checked = True
        ShowRule(New RefRule())
        _ruleTouched = False
    End Sub

    Public Sub New(id As Integer)
        MyBase.New("Edit payment method", "Change the account, the QR code or the reference rule.", Theme.S(760), Theme.S(560))
        _id = id
        Build()
        Dim row As DataRow = Nothing
        Try
            row = PaymentRepository.GetOne(id)
            If row IsNot Nothing AndAlso Convert.ToInt32(row("has_qr")) <> 0 Then _qrBytes = PaymentRepository.GetQr(id)
        Catch ex As Exception
            ShowMessage("Could not load this payment method: " & ex.Message, True)
        End Try
        If row Is Nothing Then
            _save.Enabled = False
            If Message.Text.Length = 0 Then ShowMessage("This payment method no longer exists.", True)
            Return
        End If
        _name.Value = row("name").ToString()
        _kind.Value = PaymentRepository.KindText(row("kind").ToString())
        _accName.Value = If(row("account_name") Is DBNull.Value, "", row("account_name").ToString())
        _accNumber.Value = If(row("account_number") Is DBNull.Value, "", row("account_number").ToString())
        _needsRef.Checked = Convert.ToInt32(row("needs_reference")) <> 0
        ShowRule(PaymentRepository.RuleOf(row))
        _ruleTouched = True             ' a saved rule is never replaced by a suggestion on its own
        UpdateSuggestion()
        ShowQr()
    End Sub

    ' ------------------------------------------------------------------ reference number rule
    Private Sub ShowRule(rule As RefRule)
        _settingRule = True
        _refKind.Value = If(rule.DigitsOnly, NumbersOnly, LettersAndNumbers)
        _refMin.Value = rule.MinLength.ToString()
        _refMax.Value = rule.MaxLength.ToString()
        _settingRule = False
    End Sub

    ''' <summary>The rule typed in the dialog, or Nothing when a length is not a number.</summary>
    Private Function ReadRule() As RefRule
        Dim mn, mx As Integer
        If Not Integer.TryParse(_refMin.Value.Trim(), mn) OrElse Not Integer.TryParse(_refMax.Value.Trim(), mx) Then Return Nothing
        Return New RefRule(_refKind.Value = NumbersOnly, mn, mx)
    End Function

    Private Sub OnRuleEdited()
        If _settingRule Then Return
        _ruleTouched = True
        UpdateSuggestion()
    End Sub

    ''' <summary>
    ''' A new GCash / PayPal method gets its usual format by itself (until the person changes it);
    ''' otherwise a link offers it when the fields differ.
    ''' </summary>
    Private Sub UpdateSuggestion()
        Dim s As RefRule = PaymentRepository.SuggestedRule(_name.Value)
        If s Is Nothing Then
            _suggest.Visible = False
            Return
        End If
        If _id = 0 AndAlso Not _ruleTouched Then ShowRule(s)
        Dim cur As RefRule = ReadRule()
        Dim same As Boolean = cur IsNot Nothing AndAlso cur.DigitsOnly = s.DigitsOnly AndAlso cur.MinLength = s.MinLength AndAlso cur.MaxLength = s.MaxLength
        _suggest.Text = If(same, "This is the usual " & _name.Value.Trim() & " format: " & s.Text & ".",
                           "Use the usual " & _name.Value.Trim() & " format: " & s.Text)
        _suggest.Enabled = Not same
        _suggest.Visible = True
    End Sub

    Private Sub OnSuggest(sender As Object, e As LinkLabelLinkClickedEventArgs)
        Dim s As RefRule = PaymentRepository.SuggestedRule(_name.Value)
        If s Is Nothing Then Return
        ShowRule(s)
        UpdateSuggestion()
    End Sub

    Private Sub Build()
        Dim x As Integer = Theme.S(40)
        _name.Location = New Point(x, Theme.S(108))
        _name.MaxLength = 60
        _kind.Location = New Point(x + Theme.S(316), Theme.S(108))
        Dim kinds As New System.Collections.Generic.List(Of String)()
        For Each k As String In PaymentRepository.KindCodes
            kinds.Add(PaymentRepository.KindText(k))
        Next
        _kind.SetChoices(kinds.ToArray())

        _accName.Location = New Point(x, Theme.S(184))
        _accName.MaxLength = 100
        _accNumber.Location = New Point(x + Theme.S(316), Theme.S(184))
        _accNumber.MaxLength = 30

        _needsRef.AutoSize = True
        _needsRef.Font = Theme.FontBase
        _needsRef.ForeColor = Theme.TextDark
        _needsRef.Text = "Ask the cashier for the reference number of each payment"
        _needsRef.Location = New Point(x, Theme.S(264))

        Dim hint As New Label()
        hint.AutoSize = False
        hint.Size = New Size(Theme.S(500), Theme.S(46))
        hint.Location = New Point(x + Theme.S(20), Theme.S(290))
        hint.Font = Theme.FontSmall
        hint.ForeColor = Theme.TextMuted
        hint.Text = "Recommended. Copied from the customer's confirmation (GCash: Ref No., Maya: Reference ID). " &
                    "Printed on the receipt; never accepted twice."

        ' what a correct reference number looks like: the POS refuses anything else
        _refKind.Location = New Point(x, Theme.S(340))
        _refKind.SetChoices(New String() {NumbersOnly, LettersAndNumbers})
        AddHandler _refKind.ValueChanged, AddressOf OnRuleEdited
        _refMin.Location = New Point(x + Theme.S(216), Theme.S(340))
        _refMin.MinNumber = 1D
        _refMin.MaxNumber = RefRule.Longest
        _refMin.MaxLength = 2
        AddHandler _refMin.ValueChanged, AddressOf OnRuleEdited
        _refMax.Location = New Point(x + Theme.S(332), Theme.S(340))
        _refMax.MinNumber = 1D
        _refMax.MaxNumber = RefRule.Longest
        _refMax.MaxLength = 2
        AddHandler _refMax.ValueChanged, AddressOf OnRuleEdited
        Dim lenNote As New Label()
        lenNote.AutoSize = False
        lenNote.Size = New Size(Theme.S(70), Theme.S(40))
        lenNote.Location = New Point(x + Theme.S(442), Theme.S(366))
        lenNote.Font = Theme.FontSmall
        lenNote.ForeColor = Theme.TextMuted
        lenNote.Text = "characters"
        lenNote.TextAlign = ContentAlignment.MiddleLeft
        StyleLink(_suggest, "", New Point(x, Theme.S(410)))
        _suggest.Visible = False
        AddHandler _suggest.LinkClicked, AddressOf OnSuggest
        AddHandler _name.ValueChanged, Sub() UpdateSuggestion()

        ' QR code on the right
        _qr.Location = New Point(Theme.S(560), Theme.S(108))
        _qr.Size = New Size(Theme.S(160), Theme.S(160))
        _qr.SizeMode = PictureBoxSizeMode.Zoom
        _qr.BackColor = Color.FromArgb(246, 246, 246)
        _qr.BorderStyle = BorderStyle.FixedSingle

        _qrNote.AutoSize = False
        _qrNote.Size = New Size(Theme.S(160), Theme.S(56))
        _qrNote.Location = New Point(Theme.S(560), Theme.S(272))
        _qrNote.Font = Theme.FontSmall
        _qrNote.ForeColor = Theme.TextMuted
        _qrNote.TextAlign = ContentAlignment.TopCenter

        StyleLink(_choose, "Choose QR picture...", New Point(Theme.S(560), Theme.S(330)))
        AddHandler _choose.LinkClicked, AddressOf OnChoose
        StyleLink(_remove, "Remove", New Point(Theme.S(560), Theme.S(352)))
        AddHandler _remove.LinkClicked, AddressOf OnRemove

        Message.Location = New Point(x, Theme.S(436))
        Message.Size = New Size(Theme.S(680), Theme.S(30))

        _cancel.Text = "Cancel"
        _cancel.MakeSecondary()
        _cancel.Size = New Size(Theme.S(130), Theme.S(44))
        _cancel.Location = New Point(Theme.S(720) - Theme.S(130) - Theme.S(10) - Theme.S(170), Theme.S(470))
        AddHandler _cancel.Click, Sub(s, e) DialogResult = DialogResult.Cancel

        _save.Text = If(_id = 0, "Add method", "Save changes")
        _save.Size = New Size(Theme.S(170), Theme.S(44))
        _save.Location = New Point(Theme.S(720) - Theme.S(170), Theme.S(470))
        AddHandler _save.Click, AddressOf OnSave

        Controls.AddRange(New Control() {_name, _kind, _accName, _accNumber, _needsRef, hint, _refKind, _refMin, _refMax, lenNote, _suggest,
                                         _qr, _qrNote, _choose, _remove, _cancel, _save})
        ClientSize = New Size(Theme.S(760), Theme.S(470) + Theme.S(44) + Theme.S(26))
        AcceptButton = _save
        CancelButton = _cancel
        ShowQr()
    End Sub

    Private Shared Sub StyleLink(l As LinkLabel, text As String, p As Point)
        l.AutoSize = True
        l.Text = text
        l.Font = Theme.FontSmall
        l.LinkColor = Theme.Maroon
        l.ActiveLinkColor = Theme.MaroonDark
        l.VisitedLinkColor = Theme.Maroon
        l.LinkBehavior = LinkBehavior.HoverUnderline
        l.Location = p
    End Sub

    Private Sub ShowQr()
        Dim old As Image = _qr.Image
        _qr.Image = PaymentRepository.ToImage(_qrBytes)
        If old IsNot Nothing Then old.Dispose()
        _qrNote.Text = If(_qr.Image Is Nothing, "No QR code yet. Use the QR from your GCash / Maya / bank app.", "The customer scans this at the POS.")
        _remove.Visible = (_qr.Image IsNot Nothing)
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        _name.FocusInput()
    End Sub

    Private Sub OnChoose(sender As Object, e As LinkLabelLinkClickedEventArgs)
        Using ofd As New OpenFileDialog()
            ofd.Title = "Choose the QR code picture (screenshot or download it from your GCash / Maya / bank app)"
            ofd.Filter = "Pictures (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
            If ofd.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim err As String = ""
            Dim b As Byte() = PaymentRepository.ToQrBytes(ofd.FileName, err)
            If b Is Nothing Then
                ShowMessage(err, True)
                Return
            End If
            _qrBytes = b
            _qrChanged = True
            _qrCleared = False
            ShowQr()
            ShowMessage("", False)
        End Using
    End Sub

    Private Sub OnRemove(sender As Object, e As LinkLabelLinkClickedEventArgs)
        _qrBytes = Nothing
        _qrChanged = False
        _qrCleared = True
        ShowQr()
    End Sub

    Private Sub OnSave(sender As Object, e As EventArgs)
        ShowMessage("", False)
        Dim problem As String = InputField.CheckAll(_name, _accName, _accNumber, _refMin, _refMax)
        If problem.Length = 0 Then problem = PaymentRepository.AccountNumberProblem(_accNumber.Value)
        Dim rule As RefRule = ReadRule()
        If problem.Length = 0 AndAlso rule Is Nothing Then problem = "Type the shortest and longest reference length as numbers."
        If problem.Length = 0 Then problem = rule.Problem()
        If problem.Length = 0 AndAlso String.Equals(_name.Value.Trim(), "Cash", StringComparison.OrdinalIgnoreCase) Then
            problem = "Cash already exists and is always available."
        End If
        If problem.Length > 0 Then
            ShowMessage(problem, True)
            Return
        End If

        _save.Enabled = False
        Cursor = Cursors.WaitCursor
        Try
            Dim r As SaveResult = PaymentRepository.Save(_id, _name.Value, PaymentRepository.KindFromText(_kind.Value), _accName.Value, _accNumber.Value,
                                                         _needsRef.Checked, rule, If(_qrChanged, _qrBytes, Nothing), _qrCleared)
            Select Case r
                Case SaveResult.Ok
                    AuditLog.Write(If(_id = 0, "ADD", "EDIT"), "Payment Methods",
                                   If(_id = 0, "Added", "Edited") & " payment method '" & _name.Value.Trim() & "' (reference: " & rule.Text & ")" &
                                   If(_qrChanged, " (new QR code)", If(_qrCleared, " (QR code removed)", "")))
                    DialogResult = DialogResult.OK
                Case SaveResult.Duplicate
                    ShowMessage("A payment method with this name already exists.", True)
                    _name.FocusInput()
                Case SaveResult.NotFound
                    ShowMessage("This payment method no longer exists.", True)
                Case Else
                    ShowMessage("Check the name and the account number.", True)
            End Select
        Catch ex As Exception
            ShowMessage("Could not save: " & ex.Message, True)
        Finally
            Cursor = Cursors.Default
            If DialogResult <> DialogResult.OK Then _save.Enabled = True
        End Try
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing AndAlso _qr.Image IsNot Nothing Then _qr.Image.Dispose()
        MyBase.Dispose(disposing)
    End Sub
End Class

''' <summary>
''' POS: a non-cash payment. Shows the shop's QR code, account and the exact amount; the customer pays from their app;
''' the cashier checks the customer's confirmation screen and types its reference number.
''' The app does not see the money arrive: the cashier must check the confirmation (amount, account name, reference).
''' </summary>
Public Class PaymentConfirmDialog
    Inherits ModalDialog

    Private ReadOnly _method As PaymentMethod
    Private ReadOnly _qr As New PictureBox()
    Private ReadOnly _ref As New InputField("Reference number", InputKind.Text, Theme.S(330), True)
    Private ReadOnly _ok As New MaroonButton()
    Private ReadOnly _cancel As New MaroonButton()

    ''' <summary>The reference number typed (after OK).</summary>
    Public ReadOnly Property Reference As String
        Get
            Return _ref.Value.Trim()
        End Get
    End Property

    Public Sub New(method As PaymentMethod, amount As Decimal)
        MyBase.New(method.Name & " payment", "The customer pays the exact amount from their app, then shows you the confirmation.",
                   Theme.S(720), Theme.S(480))
        _method = method
        Dim x As Integer = Theme.S(40)

        ' QR on the left
        _qr.Location = New Point(x, Theme.S(108))
        _qr.Size = New Size(Theme.S(260), Theme.S(260))
        _qr.SizeMode = PictureBoxSizeMode.Zoom
        _qr.BackColor = Color.White
        _qr.BorderStyle = BorderStyle.FixedSingle
        Try
            If method.HasQr Then _qr.Image = PaymentRepository.ToImage(PaymentRepository.GetQr(method.Id))
        Catch
            _qr.Image = Nothing
        End Try
        Controls.Add(_qr)
        If _qr.Image Is Nothing Then
            ' no QR saved: say so inside the empty box
            Dim noneText As String = "No QR code saved for " & method.Name & "." & Environment.NewLine & Environment.NewLine &
                                     "Give the customer the account number on the right."
            AddHandler _qr.Paint,
                Sub(s As Object, e As PaintEventArgs)
                    Dim r As Rectangle = _qr.ClientRectangle
                    r.Inflate(-Theme.S(20), -Theme.S(20))
                    TextRenderer.DrawText(e.Graphics, noneText, Theme.FontSmall, r, Theme.TextMuted,
                                          TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.WordBreak)
                End Sub
        End If

        ' amount, account, reference on the right
        Dim rx As Integer = x + Theme.S(260) + Theme.S(30)
        Dim y As Integer = Theme.S(106)
        AddText("AMOUNT TO PAY", Theme.FontCaption, Theme.TextMuted, rx, y, 24)
        AddText(Currency.Format(amount), Theme.MakeFont(Theme.HeadingFamily, 26.0F), Theme.Maroon, rx, y + Theme.S(20), 54)
        y += Theme.S(84)
        If method.AccountName.Length > 0 Then
            AddText("ACCOUNT NAME", Theme.FontCaption, Theme.TextMuted, rx, y, 20)
            AddText(method.AccountName, Theme.FontHeading, Theme.TextDark, rx, y + Theme.S(18), 26)
            y += Theme.S(50)
        End If
        If method.AccountNumber.Length > 0 Then
            AddText("ACCOUNT / MOBILE NUMBER", Theme.FontCaption, Theme.TextMuted, rx, y, 20)
            AddText(method.AccountNumber, Theme.FontHeading, Theme.TextDark, rx, y + Theme.S(18), 26)
            y += Theme.S(50)
        End If

        _ref.Location = New Point(rx, Math.Max(y + Theme.S(4), Theme.S(260)))
        ' only the characters this method's reference has, and never longer than it
        _ref.Kind = If(method.Rule.DigitsOnly, InputKind.Digits, InputKind.Code)
        _ref.MaxLength = method.Rule.MaxLength
        _ref.Required = method.NeedsReference
        _ref.Caption = "Reference no. (" & method.Rule.Text & If(method.NeedsReference, ")", ", optional)")
        Controls.Add(_ref)

        Dim check As New Label()
        check.AutoSize = False
        check.Size = New Size(Theme.S(640), Theme.S(40))
        check.Location = New Point(x, Theme.S(380))
        check.Font = Theme.FontSmall
        check.ForeColor = Theme.TextDark
        check.Text = "Before you continue, look at the customer's screen: the payment must say Successful / Sent, the amount must be " &
                     Currency.Format(amount) & If(method.AccountName.Length > 0, " and it must be sent to " & method.AccountName, "") & "."
        Controls.Add(check)

        Message.Location = New Point(x, Theme.S(420))
        Message.Size = New Size(Theme.S(640), Theme.S(24))

        _cancel.Text = "Back"
        _cancel.MakeSecondary()
        _cancel.Size = New Size(Theme.S(130), Theme.S(46))
        _cancel.Location = New Point(Theme.S(680) - Theme.S(130) - Theme.S(10) - Theme.S(220), Theme.S(450))
        AddHandler _cancel.Click, Sub(s, e) DialogResult = DialogResult.Cancel

        _ok.Text = "Payment received"
        _ok.Size = New Size(Theme.S(220), Theme.S(46))
        _ok.Location = New Point(Theme.S(680) - Theme.S(220), Theme.S(450))
        AddHandler _ok.Click, AddressOf OnOk

        Controls.Add(_cancel)
        Controls.Add(_ok)
        ClientSize = New Size(Theme.S(720), Theme.S(450) + Theme.S(46) + Theme.S(24))
        AcceptButton = _ok
        CancelButton = _cancel
    End Sub

    Private Sub AddText(text As String, f As Font, c As Color, x As Integer, y As Integer, h As Integer)
        Dim l As New Label()
        l.AutoSize = False
        l.AutoEllipsis = True
        l.UseMnemonic = False
        l.Size = New Size(Theme.S(350), Theme.S(h))
        l.Location = New Point(x, y)
        l.Font = f
        l.ForeColor = c
        l.Text = text
        Controls.Add(l)
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        _ref.FocusInput()
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        Dim ref As String = _ref.Value.Trim()
        If _method.NeedsReference OrElse ref.Length > 0 Then
            Dim problem As String = PaymentRepository.ReferenceProblem(ref, _method.Name, _method.Rule)
            If problem.Length > 0 Then
                ShowMessage(problem, True)
                _ref.FocusInput()
                Return
            End If
        End If
        DialogResult = DialogResult.OK
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing AndAlso _qr.Image IsNot Nothing Then _qr.Image.Dispose()
        MyBase.Dispose(disposing)
    End Sub
End Class
