Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' The two-step check before a backup, restore, delete or reset:
'''   Step 1: the logged-in Superadmin types their password (3 tries, every wrong one is audited).
'''   Step 2: they type a word (BACKUP, RESTORE, DELETE, RESET) to confirm they really mean it.
''' </summary>
Public Class SecurityCheckDialog
    Inherits ModalDialog

    Private Const MaxTries As Integer = 3

    Private ReadOnly _actionName As String
    Private ReadOnly _word As String
    Private ReadOnly _actionText As String
    Private ReadOnly _step As New Label()
    Private ReadOnly _details As New Label()
    Private ReadOnly _password As New InputField("Superadmin password", True, Theme.S(480))
    Private ReadOnly _show As New CheckBox()
    Private ReadOnly _confirm As New InputField("Confirmation", InputKind.Text, Theme.S(480), True)
    Private ReadOnly _back As New MaroonButton()
    Private ReadOnly _cancel As New MaroonButton()
    Private ReadOnly _next As New MaroonButton()
    Private _tries As Integer

    ''' <param name="actionName">Title, e.g. "Restore a backup".</param>
    ''' <param name="details">What will happen, in plain words.</param>
    ''' <param name="confirmWord">The word to type in step 2 (capital letters).</param>
    ''' <param name="actionText">Text of the final button, e.g. "Restore".</param>
    Public Sub New(actionName As String, details As String, confirmWord As String, actionText As String)
        MyBase.New(actionName, "Two steps keep this safe: your password, then a typed confirmation.", Theme.S(560), Theme.S(440))
        _actionName = actionName
        _word = confirmWord
        _actionText = actionText

        Dim x As Integer = Theme.S(40)
        Dim w As Integer = Theme.S(480)

        _step.AutoSize = True
        _step.Font = Theme.FontHeading
        _step.ForeColor = Theme.Maroon
        _step.Location = New Point(x, Theme.S(102))

        _details.AutoSize = False
        _details.Size = New Size(w, Theme.S(92))
        _details.Location = New Point(x, Theme.S(132))
        _details.Font = Theme.FontBase
        _details.ForeColor = Theme.TextDark
        _details.Text = details

        _password.Location = New Point(x, Theme.S(228))
        _password.MaxLength = 64

        _show.AutoSize = True
        _show.Text = "Show password"
        _show.Font = Theme.FontSmall
        _show.ForeColor = Theme.TextMuted
        _show.Location = New Point(x, Theme.S(300))
        AddHandler _show.CheckedChanged, Sub(s, e) _password.IsPassword = Not _show.Checked

        _confirm.Location = New Point(x, Theme.S(228))
        _confirm.MaxLength = 20
        _confirm.Visible = False
        AddHandler _confirm.ValueChanged, AddressOf OnConfirmChanged

        Message.Location = New Point(x, Theme.S(330))
        Message.Size = New Size(w, Theme.S(34))

        _back.Text = "Back"
        _back.MakeSecondary()
        _back.Size = New Size(Theme.S(110), Theme.S(44))
        _back.Location = New Point(x, Theme.S(372))
        _back.Visible = False
        AddHandler _back.Click, AddressOf OnBack

        _cancel.Text = "Cancel"
        _cancel.MakeSecondary()
        _cancel.Size = New Size(Theme.S(130), Theme.S(44))
        _cancel.Location = New Point(x + w - Theme.S(130) - Theme.S(10) - Theme.S(160), Theme.S(372))
        AddHandler _cancel.Click, Sub(s, e) DialogResult = DialogResult.Cancel

        _next.Size = New Size(Theme.S(160), Theme.S(44))
        _next.Location = New Point(x + w - Theme.S(160), Theme.S(372))
        AddHandler _next.Click, AddressOf OnNext

        Controls.AddRange(New Control() {_step, _details, _password, _show, _confirm, _back, _cancel, _next})
        ClientSize = New Size(Theme.S(560), Theme.S(372) + Theme.S(44) + Theme.S(26))
        AcceptButton = _next
        CancelButton = _cancel
        ShowStep1()
    End Sub

    Private ReadOnly Property OnStep2 As Boolean
        Get
            Return _confirm.Visible
        End Get
    End Property

    Private Sub ShowStep1()
        _step.Text = "Step 1 of 2:  your Superadmin password"
        _password.Visible = True
        _show.Visible = True
        _confirm.Visible = False
        _back.Visible = False
        _next.Text = "Next"
        _next.Enabled = True
        ShowMessage("", False)
    End Sub

    Private Sub ShowStep2()
        _step.Text = "Step 2 of 2:  type " & _word & " to confirm"
        _password.Value = ""
        _password.Visible = False
        _show.Visible = False
        _confirm.Caption = "Type " & _word & " (capital letters)"
        _confirm.Value = ""
        _confirm.Visible = True
        _back.Visible = True
        _next.Text = _actionText
        _next.Enabled = False
        ShowMessage("", False)
        _confirm.FocusInput()
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        _password.FocusInput()
    End Sub

    Private Sub OnBack(sender As Object, e As EventArgs)
        ShowStep1()
        _password.FocusInput()
    End Sub

    Private Sub OnConfirmChanged()
        _next.Enabled = (_confirm.Value.Trim() = _word)
    End Sub

    Private Sub OnNext(sender As Object, e As EventArgs)
        If OnStep2 Then
            If _confirm.Value.Trim() <> _word Then
                ShowMessage("Type " & _word & " exactly, in capital letters.", True)
                Return
            End If
            DialogResult = DialogResult.OK
            Return
        End If

        If _password.Value.Length = 0 Then
            ShowMessage("Enter your password.", True)
            _password.FocusInput()
            Return
        End If

        _next.Enabled = False
        Cursor = Cursors.WaitCursor
        Dim ok As Boolean
        Try
            ok = AuthService.VerifySuperadminPassword(_password.Value, _actionName)
        Finally
            Cursor = Cursors.Default
            _next.Enabled = True
        End Try

        If ok Then
            ShowStep2()
            Return
        End If

        _tries += 1
        _password.Value = ""
        If _tries >= MaxTries Then
            MessageBox.Show(Me, "Too many wrong passwords. Nothing was done.", _actionName, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            DialogResult = DialogResult.Cancel
            Return
        End If
        ShowMessage("Wrong password. " & (MaxTries - _tries) & " tries left.", True)
        _password.FocusInput()
    End Sub
End Class

''' <summary>Makes a new folder for the backups (inside a folder the user picks) and returns its path.</summary>
Public Class NewFolderDialog
    Inherits ModalDialog

    Private ReadOnly _parentCaption As New Label()
    Private ReadOnly _parentPath As New Label()
    Private ReadOnly _browse As New MaroonButton()
    Private ReadOnly _name As New InputField("New folder name", InputKind.Text, Theme.S(480), True)
    Private ReadOnly _create As New MaroonButton()
    Private ReadOnly _cancel As New MaroonButton()
    Private _parent As String

    ''' <summary>The folder that was made (after OK).</summary>
    Public Property CreatedPath As String = ""

    Public Sub New(startParent As String)
        MyBase.New("New backup folder", "Make a folder for the backups. It becomes the backup folder.", Theme.S(560), Theme.S(380))
        _parent = startParent
        Dim x As Integer = Theme.S(40)
        Dim w As Integer = Theme.S(480)

        _parentCaption.AutoSize = True
        _parentCaption.Font = Theme.FontCaption
        _parentCaption.ForeColor = Theme.TextMuted
        _parentCaption.Text = "Make it inside"
        _parentCaption.Location = New Point(x, Theme.S(108))

        _parentPath.AutoSize = False
        _parentPath.AutoEllipsis = True
        _parentPath.Size = New Size(w - Theme.S(130), Theme.S(40))
        _parentPath.Location = New Point(x, Theme.S(132))
        _parentPath.Font = Theme.FontBase
        _parentPath.ForeColor = Theme.TextDark
        _parentPath.TextAlign = ContentAlignment.MiddleLeft

        _browse.Text = "Browse..."
        _browse.MakeSecondary()
        _browse.Size = New Size(Theme.S(120), Theme.S(40))
        _browse.Location = New Point(x + w - Theme.S(120), Theme.S(132))
        AddHandler _browse.Click, AddressOf OnBrowse

        _name.Location = New Point(x, Theme.S(186))
        _name.MaxLength = 60

        Message.Location = New Point(x, Theme.S(262))
        Message.Size = New Size(w, Theme.S(34))

        _cancel.Text = "Cancel"
        _cancel.MakeSecondary()
        _cancel.Size = New Size(Theme.S(130), Theme.S(44))
        _cancel.Location = New Point(x + w - Theme.S(130) - Theme.S(10) - Theme.S(160), Theme.S(304))
        AddHandler _cancel.Click, Sub(s, e) DialogResult = DialogResult.Cancel

        _create.Text = "Create folder"
        _create.Size = New Size(Theme.S(160), Theme.S(44))
        _create.Location = New Point(x + w - Theme.S(160), Theme.S(304))
        AddHandler _create.Click, AddressOf OnCreate

        Controls.AddRange(New Control() {_parentCaption, _parentPath, _browse, _name, _cancel, _create})
        ClientSize = New Size(Theme.S(560), Theme.S(304) + Theme.S(44) + Theme.S(26))
        AcceptButton = _create
        CancelButton = _cancel
        ShowParent()
    End Sub

    Private Sub ShowParent()
        _parentPath.Text = _parent
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        _name.FocusInput()
    End Sub

    Private Sub OnBrowse(sender As Object, e As EventArgs)
        Using fb As New FolderBrowserDialog()
            fb.Description = "Choose where to make the new folder"
            fb.ShowNewFolderButton = True
            If Directory.Exists(_parent) Then fb.SelectedPath = _parent
            If fb.ShowDialog(Me) = DialogResult.OK Then
                _parent = fb.SelectedPath
                ShowParent()
            End If
        End Using
    End Sub

    Private Sub OnCreate(sender As Object, e As EventArgs)
        Dim problem As String = BackupService.FolderNameProblem(_name.Value)
        If problem.Length > 0 Then
            ShowMessage(problem, True)
            _name.FocusInput()
            Return
        End If
        If String.IsNullOrWhiteSpace(_parent) OrElse Not Directory.Exists(_parent) Then
            ShowMessage("Choose where to make the folder (Browse...).", True)
            Return
        End If

        Dim full As String = Path.Combine(_parent, _name.Value.Trim())
        If Directory.Exists(full) Then
            ShowMessage("A folder with this name is already there. Type another name.", True)
            _name.FocusInput()
            Return
        End If
        Try
            Directory.CreateDirectory(full)
        Catch ex As Exception
            ShowMessage("The folder could not be made: " & ex.Message, True)
            Return
        End Try
        CreatedPath = full
        DialogResult = DialogResult.OK
    End Sub
End Class

''' <summary>Full reset, or the parts to clear. Parts that other parts depend on are ticked (and locked) by themselves.</summary>
Public Class ResetOptionsDialog
    Inherits ModalDialog

    Private ReadOnly _full As New RadioButton()
    Private ReadOnly _fullNote As New Label()
    Private ReadOnly _choose As New RadioButton()
    Private ReadOnly _boxes As New List(Of CheckBox)()
    Private ReadOnly _note As New Label()
    Private ReadOnly _cancel As New MaroonButton()
    Private ReadOnly _continue As New MaroonButton()
    Private ReadOnly _picked As New HashSet(Of String)()
    Private _updating As Boolean

    Public ReadOnly Property IsFull As Boolean
        Get
            Return _full.Checked
        End Get
    End Property

    ''' <summary>Every section that will be cleared (requirements included).</summary>
    Public ReadOnly Property Chosen As List(Of String)
        Get
            Dim l As New List(Of String)()
            For Each cb As CheckBox In _boxes
                If cb.Checked Then l.Add(CStr(cb.Tag))
            Next
            Return l
        End Get
    End Property

    Public Sub New()
        MyBase.New("Reset the system", "Clear records for a fresh start. A backup of the current system is saved first.", Theme.S(800), Theme.S(640))
        Dim x As Integer = Theme.S(40)
        Dim w As Integer = Theme.S(720)

        _full.AutoSize = True
        _full.Font = Theme.FontHeading
        _full.ForeColor = Theme.TextDark
        _full.Text = "Full reset: clear every record"
        _full.Location = New Point(x, Theme.S(104))
        _full.Checked = True
        AddHandler _full.CheckedChanged, Sub(s, e) Recompute()

        _fullNote.AutoSize = False
        _fullNote.Size = New Size(w - Theme.S(24), Theme.S(22))
        _fullNote.Location = New Point(x + Theme.S(24), Theme.S(134))
        _fullNote.Font = Theme.FontSmall
        _fullNote.ForeColor = Theme.TextMuted
        _fullNote.Text = "Always kept: the Superadmin account, VAT, settings, permissions and the backup files."

        _choose.AutoSize = True
        _choose.Font = Theme.FontHeading
        _choose.ForeColor = Theme.TextDark
        _choose.Text = "Choose what to clear"
        _choose.Location = New Point(x, Theme.S(164))
        AddHandler _choose.CheckedChanged, Sub(s, e) Recompute()

        Controls.AddRange(New Control() {_full, _fullNote, _choose})

        ' two columns of parts
        Dim colW As Integer = (w - Theme.S(24) - Theme.S(20)) \ 2
        Dim top As Integer = Theme.S(202)
        Dim rowH As Integer = Theme.S(70)
        Dim i As Integer = 0
        For Each s As ResetSection In SystemReset.Sections
            Dim col As Integer = i Mod 2
            Dim row As Integer = i \ 2
            Dim left As Integer = x + Theme.S(24) + col * (colW + Theme.S(20))
            Dim y As Integer = top + row * rowH

            Dim cb As New CheckBox()
            cb.AutoSize = True
            cb.Font = Theme.MakeFont(Theme.HeadingFamily, 10.0F)
            cb.ForeColor = Theme.TextDark
            cb.Text = s.Title
            cb.Tag = s.Key
            cb.Location = New Point(left, y)
            AddHandler cb.CheckedChanged, AddressOf OnBoxChanged
            _boxes.Add(cb)

            Dim d As New Label()
            d.AutoSize = False
            d.Size = New Size(colW - Theme.S(20), Theme.S(40))
            d.Location = New Point(left + Theme.S(20), y + Theme.S(24))
            d.Font = Theme.FontSmall
            d.ForeColor = Theme.TextMuted
            d.Text = s.Description

            Controls.Add(cb)
            Controls.Add(d)
            i += 1
        Next

        Dim afterRows As Integer = top + ((SystemReset.Sections.Count + 1) \ 2) * rowH

        _note.AutoSize = False
        _note.Size = New Size(w, Theme.S(22))
        _note.Location = New Point(x, afterRows + Theme.S(2))
        _note.Font = Theme.FontSmall
        _note.ForeColor = Theme.Maroon

        Message.Location = New Point(x, afterRows + Theme.S(26))
        Message.Size = New Size(w, Theme.S(26))

        Dim by As Integer = afterRows + Theme.S(58)
        _cancel.Text = "Cancel"
        _cancel.MakeSecondary()
        _cancel.Size = New Size(Theme.S(130), Theme.S(44))
        _cancel.Location = New Point(x + w - Theme.S(130) - Theme.S(10) - Theme.S(160), by)
        AddHandler _cancel.Click, Sub(s, e) DialogResult = DialogResult.Cancel

        _continue.Text = "Continue"
        _continue.MakeDanger()
        _continue.Size = New Size(Theme.S(160), Theme.S(44))
        _continue.Location = New Point(x + w - Theme.S(160), by)
        AddHandler _continue.Click, AddressOf OnContinue

        Controls.AddRange(New Control() {_note, _cancel, _continue})
        ClientSize = New Size(Theme.S(800), by + Theme.S(44) + Theme.S(26))
        CancelButton = _cancel
        Recompute()
    End Sub

    Private Sub OnBoxChanged(sender As Object, e As EventArgs)
        If _updating Then Return
        Dim cb As CheckBox = DirectCast(sender, CheckBox)
        Dim key As String = CStr(cb.Tag)
        If cb.Checked Then
            _picked.Add(key)
        Else
            _picked.Remove(key)
        End If
        Recompute()
    End Sub

    Private Sub Recompute()
        _updating = True
        Try
            Dim locked As New List(Of String)()
            If _full.Checked Then
                For Each cb As CheckBox In _boxes
                    cb.Checked = True
                    cb.Enabled = False
                Next
                _note.Text = ""
            Else
                Dim needed As HashSet(Of String) = SystemReset.Expand(_picked)
                For Each cb As CheckBox In _boxes
                    Dim k As String = CStr(cb.Tag)
                    If _picked.Contains(k) Then
                        cb.Checked = True
                        cb.Enabled = True
                    ElseIf needed.Contains(k) Then
                        cb.Checked = True
                        cb.Enabled = False
                        locked.Add(cb.Text)
                    Else
                        cb.Checked = False
                        cb.Enabled = True
                    End If
                Next
                _note.Text = If(locked.Count = 0, "",
                                "Also cleared, because the parts you chose use them: " & String.Join(", ", locked.ToArray()) & ".")
            End If
            _continue.Enabled = Chosen.Count > 0
        Finally
            _updating = False
        End Try
    End Sub

    Private Sub OnContinue(sender As Object, e As EventArgs)
        If Chosen.Count = 0 Then
            ShowMessage("Choose at least one part to clear.", True)
            Return
        End If
        DialogResult = DialogResult.OK
    End Sub
End Class
