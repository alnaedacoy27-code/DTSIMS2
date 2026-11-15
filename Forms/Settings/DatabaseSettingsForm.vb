Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Data
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.IO
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' Settings > Database Settings (Superadmin only).
'''   Backup folder:  choose, make a new one, open it.
'''   Create backup:  saves everything into one .sql file.
'''   Saved backups:  restore (the current system is backed up first) or delete (to the Recycle Bin).
'''   Reset:          full or chosen parts (the current system is backed up first).
''' Every action asks for the Superadmin password and a typed confirmation word.
''' </summary>
<System.ComponentModel.DesignerCategory("Code")>
Public Class DatabaseSettingsForm
    Inherits Form
    Implements ILiveForm

    ''' <summary>A restore finished: the main window logs out (the users may have changed).</summary>
    Public Event RestoreFinished()

    Private Shared ReadOnly Green As Color = Color.FromArgb(22, 130, 60)

    Private ReadOnly _header As New Panel()
    Private ReadOnly _title As New Label()
    Private ReadOnly _subtitle As New Label()
    Private ReadOnly _busyText As New Label()
    Private ReadOnly _busyBar As New ProgressBar()

    Private ReadOnly _body As New Panel()

    ' backup folder
    Private ReadOnly _cardFolder As Panel = MakeCard()
    Private ReadOnly _lblFolder As New Label()
    Private ReadOnly _lblFolderState As New Label()
    Private ReadOnly _btnChange As New MaroonButton()
    Private ReadOnly _btnNewFolder As New MaroonButton()
    Private ReadOnly _btnOpen As New MaroonButton()

    ' create
    Private ReadOnly _cardCreate As Panel = MakeCard()
    Private ReadOnly _lblCreate As New Label()
    Private ReadOnly _note As New InputField("Note (optional), e.g. End of November", InputKind.Text, Theme.S(300), False)
    Private ReadOnly _btnCreate As New MaroonButton()

    ' reset
    Private ReadOnly _cardReset As Panel = MakeCard()
    Private ReadOnly _lblReset As New Label()
    Private ReadOnly _btnReset As New MaroonButton()

    ' list
    Private ReadOnly _cardList As Panel = MakeCard()
    Private ReadOnly _lblCount As New Label()
    Private ReadOnly _grid As New DataGridView()
    Private ReadOnly _lblDetail As New Label()
    Private ReadOnly _btnRestore As New MaroonButton()
    Private ReadOnly _btnRestoreFile As New MaroonButton()
    Private ReadOnly _btnDelete As New MaroonButton()
    Private ReadOnly _btnRefresh As New MaroonButton()

    Private ReadOnly _captions As New List(Of Label)()
    Private _folder As String = ""
    Private _backups As New List(Of BackupInfo)()

    Public Sub New()
        Me.AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi
        BackColor = Color.White
        Font = Theme.FontBase

        ' ---------------------------------------------------------------- header
        _header.Dock = DockStyle.Top
        _header.Height = Theme.S(78)
        _header.BackColor = Color.White

        _title.AutoSize = True
        _title.Font = Theme.FontTitle
        _title.ForeColor = Theme.Maroon
        _title.Text = "Database Settings"
        _title.Location = New Point(Theme.S(28), Theme.S(10))

        _subtitle.AutoSize = True
        _subtitle.Font = Theme.FontBase
        _subtitle.ForeColor = Theme.TextMuted
        _subtitle.Text = "Back up, restore and reset the system. Every action asks for your password and a typed confirmation."
        _subtitle.Location = New Point(Theme.S(31), Theme.S(48))

        _busyText.AutoSize = False
        _busyText.TextAlign = ContentAlignment.MiddleRight
        _busyText.Font = Theme.FontCaption
        _busyText.ForeColor = Theme.Maroon
        _busyText.Size = New Size(Theme.S(380), Theme.S(22))
        _busyText.Visible = False

        _busyBar.Style = ProgressBarStyle.Marquee
        _busyBar.MarqueeAnimationSpeed = 30
        _busyBar.Size = New Size(Theme.S(220), Theme.S(8))
        _busyBar.Visible = False

        _header.Controls.AddRange(New Control() {_title, _subtitle, _busyText, _busyBar})
        AddHandler _header.Resize, Sub(s, e) PlaceBusy()

        ' ---------------------------------------------------------------- body
        _body.Dock = DockStyle.Fill
        _body.AutoScroll = True
        _body.BackColor = Color.White
        AddHandler _body.Resize, Sub(s, e) LayoutCards()

        BuildFolderCard()
        BuildCreateCard()
        BuildResetCard()
        BuildListCard()

        _body.Controls.AddRange(New Control() {_cardFolder, _cardCreate, _cardReset, _cardList})
        Controls.Add(_body)
        Controls.Add(_header)
    End Sub

    ' ==================================================================== building
    Private Shared Function MakeCard() As Panel
        Dim p As New Panel()
        p.BackColor = Color.White
        AddHandler p.Paint,
            Sub(sender As Object, e As PaintEventArgs)
                Dim c As Panel = DirectCast(sender, Panel)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using path As GraphicsPath = Theme.RoundedRect(New Rectangle(0, 0, c.Width - 1, c.Height - 1), 12)
                    Using pen As New Pen(Theme.BorderColor, 1.0F)
                        e.Graphics.DrawPath(pen, path)
                    End Using
                End Using
            End Sub
        Return p
    End Function

    Private Sub AddCaption(card As Panel, text As String)
        Dim l As New Label()
        l.AutoSize = True
        l.Font = Theme.FontCaption
        l.ForeColor = Theme.TextMuted
        l.Text = text
        l.Location = New Point(Theme.S(20), Theme.S(16))
        card.Controls.Add(l)
        _captions.Add(l)
    End Sub

    Private Shared Sub StyleText(l As Label, f As Font, c As Color)
        l.AutoSize = False
        l.Font = f
        l.ForeColor = c
    End Sub

    Private Shared Sub StyleButton(b As MaroonButton, text As String, width As Integer, kind As String)
        b.Text = text
        b.Size = New Size(Theme.S(width), Theme.S(40))
        If kind = "secondary" Then b.MakeSecondary()
        If kind = "danger" Then b.MakeDanger()
    End Sub

    Private Sub BuildFolderCard()
        AddCaption(_cardFolder, "BACKUP FOLDER")
        StyleText(_lblFolder, Theme.FontHeading, Theme.TextDark)
        _lblFolder.AutoEllipsis = True
        _lblFolder.Location = New Point(Theme.S(20), Theme.S(38))
        StyleText(_lblFolderState, Theme.FontSmall, Theme.TextMuted)
        _lblFolderState.Location = New Point(Theme.S(20), Theme.S(66))

        StyleButton(_btnChange, "Change folder", 150, "secondary")
        StyleButton(_btnNewFolder, "New folder", 130, "secondary")
        StyleButton(_btnOpen, "Open folder", 130, "secondary")
        AddHandler _btnChange.Click, AddressOf OnChangeFolder
        AddHandler _btnNewFolder.Click, AddressOf OnNewFolder
        AddHandler _btnOpen.Click, AddressOf OnOpenFolder

        _cardFolder.Controls.AddRange(New Control() {_lblFolder, _lblFolderState, _btnChange, _btnNewFolder, _btnOpen})
        _cardFolder.Height = Theme.S(150)
    End Sub

    Private Sub BuildCreateCard()
        AddCaption(_cardCreate, "CREATE A BACKUP")
        StyleText(_lblCreate, Theme.FontBase, Theme.TextDark)
        _lblCreate.Location = New Point(Theme.S(20), Theme.S(38))
        _lblCreate.Text = "Saves every record (products, stock, sales, customers, users, settings) into one .sql file in the backup folder."

        _note.Location = New Point(Theme.S(20), Theme.S(84))
        _note.MaxLength = 100
        _note.SwallowEnter = True
        AddHandler _note.EnterPressed, Sub() OnCreate(Me, EventArgs.Empty)

        StyleButton(_btnCreate, "Create backup", 160, "primary")
        _btnCreate.Height = Theme.S(42)
        AddHandler _btnCreate.Click, AddressOf OnCreate

        _cardCreate.Controls.AddRange(New Control() {_lblCreate, _note, _btnCreate})
        _cardCreate.Height = Theme.S(170)
    End Sub

    Private Sub BuildResetCard()
        AddCaption(_cardReset, "RESET THE SYSTEM")
        StyleText(_lblReset, Theme.FontBase, Theme.TextDark)
        _lblReset.Location = New Point(Theme.S(20), Theme.S(38))
        _lblReset.Text = "Start fresh when the system is slow or full of old records: clear everything, or only the parts you choose. " &
                         "The current system is backed up first and the Superadmin account is always kept."

        StyleButton(_btnReset, "Reset system...", 170, "danger")
        AddHandler _btnReset.Click, AddressOf OnReset

        _cardReset.Controls.AddRange(New Control() {_lblReset, _btnReset})
        _cardReset.Height = Theme.S(160)
    End Sub

    Private Sub BuildListCard()
        AddCaption(_cardList, "SAVED BACKUPS")
        _lblCount.AutoSize = False
        _lblCount.TextAlign = ContentAlignment.MiddleRight
        _lblCount.Font = Theme.FontSmall
        _lblCount.ForeColor = Theme.TextMuted

        GridStyle.Apply(_grid, 44)
        _grid.Columns.Add(TextColumn("file", "File", 34))
        _grid.Columns.Add(TextColumn("created", "Made on", 22))
        _grid.Columns.Add(TextColumn("kind", "Type", 14))
        _grid.Columns.Add(TextColumn("size", "Size", 10))
        _grid.Columns.Add(TextColumn("note", "Note", 20))
        AddHandler _grid.SelectionChanged, Sub(s, e) UpdateSelection()
        AddHandler _grid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then OnRestore(Me, EventArgs.Empty)

        StyleText(_lblDetail, Theme.FontSmall, Theme.TextMuted)

        StyleButton(_btnRestore, "Restore", 120, "primary")
        StyleButton(_btnRestoreFile, "Restore from file...", 180, "secondary")
        StyleButton(_btnDelete, "Delete", 110, "danger")
        StyleButton(_btnRefresh, "Refresh", 110, "secondary")
        AddHandler _btnRestore.Click, AddressOf OnRestore
        AddHandler _btnRestoreFile.Click, AddressOf OnRestoreFromFile
        AddHandler _btnDelete.Click, AddressOf OnDelete
        AddHandler _btnRefresh.Click, Sub(s, e) LoadAll()

        _cardList.Controls.AddRange(New Control() {_lblCount, _grid, _lblDetail, _btnRestore, _btnRestoreFile, _btnDelete, _btnRefresh})
    End Sub

    Private Shared Function TextColumn(name As String, header As String, weight As Single) As DataGridViewTextBoxColumn
        Dim c As New DataGridViewTextBoxColumn()
        c.Name = name
        c.HeaderText = header
        c.FillWeight = weight
        c.SortMode = DataGridViewColumnSortMode.NotSortable
        Return c
    End Function

    ' ==================================================================== layout
    Private Sub PlaceBusy()
        Dim right As Integer = _header.ClientSize.Width - Theme.S(28)
        _busyText.Location = New Point(right - _busyText.Width, Theme.S(16))
        _busyBar.Location = New Point(right - _busyBar.Width, Theme.S(44))
    End Sub

    ''' <summary>Two columns that fill the width: actions on the left, the backup list on the right.</summary>
    Private Sub LayoutCards()
        Dim m As Integer = Theme.S(24)
        Dim gap As Integer = Theme.S(16)
        Dim avail As Integer = Math.Max(Theme.S(900), _body.ClientSize.Width) - 2 * m - gap
        Dim leftW As Integer = Math.Max(Theme.S(420), CInt(avail * 0.42))
        Dim rightW As Integer = Math.Max(Theme.S(440), avail - leftW)
        Dim top As Integer = Theme.S(6)

        _cardFolder.SetBounds(m, top, leftW, _cardFolder.Height)
        _cardCreate.SetBounds(m, _cardFolder.Bottom + gap, leftW, _cardCreate.Height)
        _cardReset.SetBounds(m, _cardCreate.Bottom + gap, leftW, _cardReset.Height)

        Dim leftBottom As Integer = _cardReset.Bottom
        Dim listH As Integer = Math.Max(leftBottom - top, _body.ClientSize.Height - top - m)
        _cardList.SetBounds(m + leftW + gap, top, rightW, listH)
        _body.AutoScrollMinSize = New Size(m * 2 + leftW + gap + rightW, Math.Max(leftBottom, _cardList.Bottom) + m)

        ' inside the left cards
        Dim inner As Integer = leftW - Theme.S(40)
        _lblFolder.Size = New Size(inner, Theme.S(26))
        _lblFolderState.Size = New Size(inner, Theme.S(20))
        _btnChange.Location = New Point(Theme.S(20), Theme.S(94))
        _btnNewFolder.Location = New Point(_btnChange.Right + Theme.S(10), Theme.S(94))
        _btnOpen.Location = New Point(_btnNewFolder.Right + Theme.S(10), Theme.S(94))

        _lblCreate.Size = New Size(inner, Theme.S(42))
        _btnCreate.Location = New Point(leftW - Theme.S(20) - _btnCreate.Width, Theme.S(106))
        Dim noteW As Integer = Math.Max(Theme.S(160), _btnCreate.Left - Theme.S(12) - Theme.S(20))
        If _note.Width <> noteW Then
            ' InputField sizes its box once: rebuild the width by resizing the panel and the box together
            ResizeInput(_note, noteW)
        End If

        _lblReset.Size = New Size(inner, Theme.S(62))
        _btnReset.Location = New Point(Theme.S(20), Theme.S(104))

        ' inside the list card
        Dim lw As Integer = rightW - Theme.S(40)
        _lblCount.SetBounds(rightW - Theme.S(20) - Theme.S(200), Theme.S(12), Theme.S(200), Theme.S(22))
        Dim btnY As Integer = listH - Theme.S(20) - Theme.S(40)
        _lblDetail.SetBounds(Theme.S(20), btnY - Theme.S(30), lw, Theme.S(22))
        _grid.SetBounds(Theme.S(20), Theme.S(42), lw, Math.Max(Theme.S(120), _lblDetail.Top - Theme.S(8) - Theme.S(42)))
        _btnRestore.Location = New Point(Theme.S(20), btnY)
        _btnRestoreFile.Location = New Point(_btnRestore.Right + Theme.S(10), btnY)
        _btnRefresh.Location = New Point(rightW - Theme.S(20) - _btnRefresh.Width, btnY)
        _btnDelete.Location = New Point(_btnRefresh.Left - Theme.S(10) - _btnDelete.Width, btnY)
    End Sub

    ''' <summary>InputField draws its box at the width it was made with; keep the panel, box and text box in step.</summary>
    Private Shared Sub ResizeInput(f As InputField, width As Integer)
        Dim delta As Integer = width - f.Width
        f.Width = width
        For Each c As Control In f.Controls
            If TypeOf c Is Panel Then
                c.Width += delta
                For Each inner As Control In c.Controls
                    inner.Width += delta
                Next
                c.Invalidate()
            End If
        Next
    End Sub

    ' ==================================================================== data
    Public ReadOnly Property WatchedTables As String() Implements ILiveForm.WatchedTables
        Get
            Return New String() {"settings", "backup_log"}
        End Get
    End Property

    Public Sub RefreshData() Implements ILiveForm.RefreshData
        If Not BackupService.IsBusy Then LoadAll()
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        PlaceBusy()
        LayoutCards()
        LoadAll()
    End Sub

    Private Sub LoadAll(Optional selectPath As String = Nothing)
        _folder = BackupService.GetFolder()
        _lblFolder.Text = _folder
        LoadBackups(selectPath)
    End Sub

    Private Sub LoadBackups(Optional selectPath As String = Nothing)
        Dim keep As String = If(selectPath, SelectedPath())
        Dim readError As String = ""
        Try
            _backups = BackupService.ListBackups(_folder)
        Catch ex As Exception
            _backups = New List(Of BackupInfo)()
            readError = ex.Message
        End Try

        _lblFolderState.ForeColor = Theme.TextMuted
        If readError.Length > 0 Then
            _lblFolderState.ForeColor = Theme.AlertRed
            _lblFolderState.Text = "The folder cannot be read: " & readError
        ElseIf Directory.Exists(_folder) Then
            _lblFolderState.Text = If(_backups.Count = 1, "1 backup in this folder", _backups.Count & " backups in this folder")
        Else
            _lblFolderState.Text = "Not on this computer yet: it is made with the first backup."
        End If
        _btnOpen.Enabled = Directory.Exists(_folder)

        _grid.Rows.Clear()
        For Each b As BackupInfo In _backups
            Dim i As Integer = _grid.Rows.Add(b.FileName, b.CreatedAt.ToString("MMM d, yyyy  h:mm tt"), b.KindDisplay, b.SizeDisplay, b.Note)
            _grid.Rows(i).Tag = b
            If b.Kind <> "manual" Then _grid.Rows(i).DefaultCellStyle.ForeColor = Theme.TextMuted
        Next
        _lblCount.Text = If(_backups.Count = 0, "", If(_backups.Count = 1, "1 backup", _backups.Count & " backups"))

        _grid.ClearSelection()
        For Each r As DataGridViewRow In _grid.Rows
            Dim b As BackupInfo = DirectCast(r.Tag, BackupInfo)
            If keep IsNot Nothing AndAlso String.Equals(b.FilePath, keep, StringComparison.OrdinalIgnoreCase) Then
                r.Selected = True
                _grid.CurrentCell = r.Cells(0)
                Exit For
            End If
        Next
        If _grid.SelectedRows.Count = 0 AndAlso _grid.Rows.Count > 0 Then _grid.Rows(0).Selected = True
        UpdateSelection()
    End Sub

    Private Function SelectedBackup() As BackupInfo
        If _grid.SelectedRows.Count = 0 Then Return Nothing
        Return TryCast(_grid.SelectedRows(0).Tag, BackupInfo)
    End Function

    Private Function SelectedPath() As String
        Dim b As BackupInfo = SelectedBackup()
        Return If(b Is Nothing, Nothing, b.FilePath)
    End Function

    Private Sub UpdateSelection()
        Dim b As BackupInfo = SelectedBackup()
        Dim idle As Boolean = Not BackupService.IsBusy
        _btnRestore.Enabled = idle AndAlso b IsNot Nothing
        _btnDelete.Enabled = idle AndAlso b IsNot Nothing
        If b Is Nothing Then
            _lblDetail.Text = If(_backups.Count = 0, "No backups yet. Create one on the left.", "")
        Else
            _lblDetail.Text = b.RowCount.ToString("N0") & " records in " & b.TableCount & " tables" &
                              If(b.CreatedBy.Length > 0, "  |  by " & b.CreatedBy, "") &
                              "  |  database version " & b.SchemaVersion
        End If
    End Sub

    ' ==================================================================== running a job
    Private Sub SetBusy(busy As Boolean, text As String)
        _busyText.Text = text
        _busyText.Visible = busy
        _busyBar.Visible = busy
        _body.Enabled = Not busy
        UseWaitCursor = busy
        PlaceBusy()
        If Not busy Then UpdateSelection()
    End Sub

    ''' <summary>Runs <paramref name="work"/> off the screen thread (the window stays responsive). Returns the error, or Nothing.</summary>
    Private Async Function RunJob(text As String, work As Action(Of Action(Of String))) As Task(Of Exception)
        If Not BackupService.TryBegin() Then
            Return New BackupException("Another database task is still running. Wait for it to finish.")
        End If
        SetBusy(True, text)
        Dim report As Action(Of String) =
            Sub(t As String)
                If IsDisposed OrElse Not IsHandleCreated Then Return
                BeginInvoke(New Action(Sub() _busyText.Text = t))
            End Sub
        Try
            Await Task.Run(Sub() work(report))
            Return Nothing
        Catch ex As Exception
            Return ex
        Finally
            BackupService.EndJob()
            SetBusy(False, "")
        End Try
    End Function

    Private Function Ask(actionName As String, details As String, word As String, buttonText As String) As Boolean
        Using d As New SecurityCheckDialog(actionName, details, word, buttonText)
            Return d.ShowDialog(OwnerOf(Me)) = DialogResult.OK
        End Using
    End Function

    Private Sub ShowError(title As String, ex As Exception)
        Dim msg As String = If(TypeOf ex Is BackupException, ex.Message, "Something went wrong: " & ex.Message)
        Notify(Me, title, msg, MessageBoxIcon.Error)
    End Sub

    ' ==================================================================== folder
    Private Sub OnChangeFolder(sender As Object, e As EventArgs)
        Using fb As New FolderBrowserDialog()
            fb.Description = "Choose the folder where the backups are saved. Use ""Make New Folder"" to add one."
            fb.ShowNewFolderButton = True
            If Directory.Exists(_folder) Then fb.SelectedPath = _folder
            If fb.ShowDialog(OwnerOf(Me)) <> DialogResult.OK Then Return
            SaveFolder(fb.SelectedPath)
        End Using
    End Sub

    Private Sub OnNewFolder(sender As Object, e As EventArgs)
        Dim start As String = If(Directory.Exists(_folder), _folder, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))
        Using d As New NewFolderDialog(start)
            If d.ShowDialog(OwnerOf(Me)) <> DialogResult.OK Then Return
            SaveFolder(d.CreatedPath)
        End Using
    End Sub

    Private Sub SaveFolder(path As String)
        If String.Equals(path, _folder, StringComparison.OrdinalIgnoreCase) Then Return
        Try
            BackupService.SetFolder(path)
            AuditLog.Write("EDIT", "Database Settings", "Backup folder changed to " & path)
        Catch ex As Exception
            ShowError("Backup folder", ex)
            Return
        End Try
        LoadAll()
    End Sub

    Private Sub OnOpenFolder(sender As Object, e As EventArgs)
        If Not Directory.Exists(_folder) Then Return
        Try
            System.Diagnostics.Process.Start("explorer.exe", """" & _folder & """")
        Catch ex As Exception
            ShowError("Open folder", ex)
        End Try
    End Sub

    ' ==================================================================== create
    Private Async Sub OnCreate(sender As Object, e As EventArgs)
        If BackupService.IsBusy Then Return
        Dim problem As String = InputField.CheckAll(_note)
        If problem.Length > 0 Then
            Notify(Me, "Create a backup", problem, MessageBoxIcon.Warning)
            Return
        End If
        Dim folder As String = _folder
        Dim note As String = _note.Value.Trim()

        If Not Ask("Create a backup",
                   "Every record is saved into a new file in:" & Environment.NewLine & folder & Environment.NewLine & Environment.NewLine &
                   "The file holds all data, password hashes included: keep it safe.",
                   "BACKUP", "Create backup") Then Return

        Dim made As BackupInfo = Nothing
        Dim err As Exception = Await RunJob("Creating the backup...",
            Sub(report As Action(Of String))
                made = BackupService.CreateBackup(folder, "manual", note, report)
            End Sub)
        If err IsNot Nothing Then
            ShowError("Create a backup", err)
            Return
        End If

        AuditLog.Write("BACKUP", "Database Settings", "Backup created: " & made.FileName)
        _note.Value = ""
        LoadAll(made.FilePath)
        Notify(Me, "Backup saved",
               "The backup was saved." & Environment.NewLine & Environment.NewLine & made.FilePath & Environment.NewLine &
               made.RowCount.ToString("N0") & " records in " & made.TableCount & " tables (" & made.SizeDisplay & ").",
               MessageBoxIcon.Information)
    End Sub

    ' ==================================================================== restore
    Private Sub OnRestore(sender As Object, e As EventArgs)
        Dim b As BackupInfo = SelectedBackup()
        If b Is Nothing OrElse BackupService.IsBusy Then Return
        RestoreFile(b.FilePath)
    End Sub

    Private Sub OnRestoreFromFile(sender As Object, e As EventArgs)
        If BackupService.IsBusy Then Return
        Using ofd As New OpenFileDialog()
            ofd.Title = "Choose a DTSIMS2 backup to restore"
            ofd.Filter = "DTSIMS2 backup (*.sql)|*.sql"
            ofd.CheckFileExists = True
            If Directory.Exists(_folder) Then ofd.InitialDirectory = _folder
            If ofd.ShowDialog(OwnerOf(Me)) <> DialogResult.OK Then Return
            RestoreFile(ofd.FileName)
        End Using
    End Sub

    Private Async Sub RestoreFile(file As String)
        Dim problem As String
        Cursor = Cursors.WaitCursor
        Try
            problem = BackupService.ValidateForRestore(file)
        Catch ex As Exception
            problem = "The backup file cannot be read: " & ex.Message
        Finally
            Cursor = Cursors.Default
        End Try
        If problem.Length > 0 Then
            Notify(Me, "Restore a backup", problem, MessageBoxIcon.Warning)
            Return
        End If

        Dim info As BackupInfo = BackupService.ReadInfo(file)
        Dim folder As String = _folder
        If Not Ask("Restore a backup",
                   "The system goes back to the backup of " & info.CreatedAt.ToString("MMM d, yyyy h:mm tt") &
                   " (" & info.RowCount.ToString("N0") & " records). Everything after it is replaced." & Environment.NewLine &
                   "First the current system is backed up (required). Then everyone is logged out. " &
                   "Ask the other laptops to stop working until it is done.",
                   "RESTORE", "Restore") Then Return

        Dim safety As BackupInfo = Nothing
        Dim err As Exception = Await RunJob("Restoring...",
            Sub(report As Action(Of String))
                report("Backing up the current system first...")
                safety = BackupService.CreateBackup(folder, "pre_restore", "Automatic backup before restoring " & info.FileName, report)
                BackupService.Restore(file, report)
            End Sub)

        If err IsNot Nothing Then
            If safety IsNot Nothing Then
                Notify(Me, "Restore a backup", err.Message & Environment.NewLine & Environment.NewLine &
                       "The backup of the current system is kept: " & safety.FileName, MessageBoxIcon.Error)
            Else
                Notify(Me, "Restore a backup", "The current system could not be backed up, so nothing was restored." &
                       Environment.NewLine & Environment.NewLine & err.Message, MessageBoxIcon.Error)
            End If
            LoadAll()
            Return
        End If

        AuditLog.Write("RESTORE", "Database Settings", "Restored " & info.FileName & " (the system before it was saved as " & safety.FileName & ")")
        Notify(Me, "Restore finished",
               "The system was restored to " & info.CreatedAt.ToString("MMM d, yyyy h:mm tt") & "." & Environment.NewLine &
               "How it was before is saved as " & safety.FileName & "." & Environment.NewLine & Environment.NewLine &
               "You will now be logged out. Log in again to continue.", MessageBoxIcon.Information)
        RaiseEvent RestoreFinished()
    End Sub

    ' ==================================================================== delete
    Private Async Sub OnDelete(sender As Object, e As EventArgs)
        Dim b As BackupInfo = SelectedBackup()
        If b Is Nothing OrElse BackupService.IsBusy Then Return
        If Not Ask("Delete a backup",
                   b.FileName & Environment.NewLine & "made " & b.CreatedAt.ToString("MMM d, yyyy h:mm tt") & " (" & b.SizeDisplay & ")" &
                   Environment.NewLine & Environment.NewLine & "It is moved to the Recycle Bin of this computer.",
                   "DELETE", "Delete backup") Then Return

        Dim folder As String = _folder
        Dim err As Exception = Await RunJob("Deleting the backup...",
            Sub(report As Action(Of String))
                BackupService.DeleteBackup(b.FilePath, folder)
            End Sub)
        If err IsNot Nothing Then
            ShowError("Delete a backup", err)
            Return
        End If
        AuditLog.Write("DELETE", "Database Settings", "Backup deleted: " & b.FileName)
        LoadAll()
    End Sub

    ' ==================================================================== reset
    Private Async Sub OnReset(sender As Object, e As EventArgs)
        If BackupService.IsBusy Then Return
        Dim chosen As List(Of String)
        Dim isFull As Boolean
        Using d As New ResetOptionsDialog()
            If d.ShowDialog(OwnerOf(Me)) <> DialogResult.OK Then Return
            chosen = d.Chosen
            isFull = d.IsFull
        End Using

        Dim titles As New List(Of String)()
        For Each k As String In chosen
            titles.Add(SystemReset.Find(k).Title)
        Next
        Dim what As String = If(isFull, "FULL RESET: every record is cleared.", "Cleared: " & String.Join(", ", titles.ToArray()) & ".")
        Dim folder As String = _folder
        If Not Ask("Reset the system",
                   what & Environment.NewLine &
                   "Kept: the Superadmin account, VAT, settings, permissions and the backups." & Environment.NewLine &
                   "First the current system is backed up (required).",
                   "RESET", "Reset now") Then Return

        Dim safety As BackupInfo = Nothing
        Dim err As Exception = Await RunJob("Resetting...",
            Sub(report As Action(Of String))
                report("Backing up the current system first...")
                safety = BackupService.CreateBackup(folder, "pre_reset", "Automatic backup before a system reset", report)
                SystemReset.Run(chosen, report)
            End Sub)

        If err IsNot Nothing Then
            If safety IsNot Nothing Then
                Notify(Me, "Reset the system", err.Message & Environment.NewLine & Environment.NewLine &
                       "The backup of the current system is kept: " & safety.FileName, MessageBoxIcon.Error)
            Else
                Notify(Me, "Reset the system", "The current system could not be backed up, so nothing was cleared." &
                       Environment.NewLine & Environment.NewLine & err.Message, MessageBoxIcon.Error)
            End If
            LoadAll()
            Return
        End If

        AuditLog.Write("RESET", "Database Settings",
                       If(isFull, "Full system reset", "System reset: " & String.Join(", ", titles.ToArray())) &
                       " (the system before it was saved as " & safety.FileName & ")")
        LoadAll(safety.FilePath)
        Notify(Me, "Reset finished",
               If(isFull, "The system was fully reset.", "Cleared: " & String.Join(", ", titles.ToArray()) & ".") & Environment.NewLine &
               "How it was before is saved as " & safety.FileName & ": restore it from the list if you need it back.",
               MessageBoxIcon.Information)
    End Sub

End Class
