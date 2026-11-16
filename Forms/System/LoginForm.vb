Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Windows.Forms

Public Class LoginForm
    Inherits AuthShellForm

    Private ReadOnly _username As New InputField("Username")
    Private ReadOnly _password As New InputField("Password", True)
    Private ReadOnly _show As New CheckBox()
    Private ReadOnly _login As New MaroonButton()
    Private ReadOnly _settingsLink As New LinkLabel()
    Private ReadOnly _exitLink As New LinkLabel()

    Public Sub New()
        MyBase.New()
        Text = "Log in - Darwin Trading"

        AddHeading("Welcome back", "Log in to continue.")

        _username.Location = New Point(LeftMargin, Theme.S(150))
        _password.Location = New Point(LeftMargin, Theme.S(228))
        _username.MaxLength = 30
        _password.MaxLength = 64

        _show.Text = "Show password"
        _show.AutoSize = True
        _show.Font = Theme.FontSmall
        _show.ForeColor = Theme.TextMuted
        _show.Location = New Point(LeftMargin, Theme.S(306))
        AddHandler _show.CheckedChanged, AddressOf OnShowChanged

        Message.Location = New Point(LeftMargin, Theme.S(338))

        _login.Text = "Log in"
        _login.Size = New Size(FieldWidth, Theme.S(46))
        _login.Location = New Point(LeftMargin, Theme.S(386))
        AddHandler _login.Click, AddressOf OnLogin

        AddLink(_settingsLink, "Server settings", LeftMargin, 560)
        AddHandler _settingsLink.LinkClicked, AddressOf OnServerSettings
        AddLink(_exitLink, "Exit", LeftMargin + FieldWidth - 24, 560)
        AddHandler _exitLink.LinkClicked, AddressOf OnExit

        Body.Controls.Add(_username)
        Body.Controls.Add(_password)
        Body.Controls.Add(_show)
        Body.Controls.Add(Message)
        Body.Controls.Add(_login)

        AcceptButton = _login
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        _username.FocusInput()
    End Sub

    Private Sub OnShowChanged(sender As Object, e As EventArgs)
        _password.IsPassword = Not _show.Checked
    End Sub

    Private Sub OnExit(sender As Object, e As LinkLabelLinkClickedEventArgs)
        DialogResult = DialogResult.Cancel
        Close()
    End Sub

    Private Sub OnServerSettings(sender As Object, e As LinkLabelLinkClickedEventArgs)
        Using f As New ConnectionSettingsForm("")
            If f.ShowDialog(Me) = DialogResult.OK Then
                Dim msg As String = ""
                If Db.CheckStatus(msg) = DbStatus.Ok Then
                    ShowMessage("Connected to " & Db.Settings.Server & ".", False)
                Else
                    ShowMessage(msg, True)
                End If
            End If
        End Using
    End Sub

    ''' <summary>
    ''' One account, one computer: when this account is logged in somewhere else right now, say so and ask.
    ''' Continuing logs the other computer off (it shows why). False = the person chose not to continue.
    ''' </summary>
    Private Function ConfirmOneComputer() As Boolean
        Dim devices As System.Collections.Generic.List(Of String)
        Try
            devices = SessionTracker.LiveDevicesOf(AppSession.UserId)
        Catch
            Return True                     ' cannot check: do not block the log-in
        End Try
        If devices.Count = 0 Then Return True

        Dim names As New System.Collections.Generic.List(Of String)()
        For Each d As String In devices
            names.Add(If(String.Equals(d, Environment.MachineName, StringComparison.OrdinalIgnoreCase) OrElse d.Length = 0,
                         "another window on this computer", d))
        Next
        Dim where As String = String.Join(", ", names.ToArray())
        Dim answer As DialogResult = MessageBox.Show(Me,
            "This account is already logged in on " & where & "." & Environment.NewLine & Environment.NewLine &
            "One account can only be used on one computer at a time. If you continue, it will be logged off there " &
            "(anything not saved on that computer is lost)." & Environment.NewLine & Environment.NewLine &
            "Log in here and log off the other computer?",
            "Already logged in", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
        If answer = DialogResult.Yes Then
            AuditLog.Write("LOGIN", "Login", "'" & AppSession.Username & "' logged in here and logged off " & where)
            Return True
        End If
        AuditLog.Write("LOGIN_CANCELLED", "Login", "'" & AppSession.Username & "' did not log in: already logged in on " & where)
        ShowMessage("Not logged in. The account stays logged in on " & where & ".", True)
        Return False
    End Function

    Private Sub OnLogin(sender As Object, e As EventArgs)
        ShowMessage("", False)
        _login.Enabled = False
        Cursor = Cursors.WaitCursor
        Try
            Dim msg As String = ""
            If AuthService.TryLogin(_username.Value, _password.Value, msg) Then
                If Not ConfirmOneComputer() Then
                    AppSession.Clear()
                    _password.Value = ""
                    Return
                End If
                DialogResult = DialogResult.OK
            Else
                ShowMessage(msg, True)
                _password.Value = ""
                _password.FocusInput()
            End If
        Finally
            Cursor = Cursors.Default
            _login.Enabled = True
        End Try
    End Sub

End Class
