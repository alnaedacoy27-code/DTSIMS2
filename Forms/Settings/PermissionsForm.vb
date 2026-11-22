Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Data
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Text
Imports System.Windows.Forms

''' <summary>
''' Settings > Permission Maintenance (Superadmin only).
'''   By role:  switch a module on or off for every Admin or every Cashier.
'''   By user:  give one person access the role does not have (Allow), take one away (Deny),
'''             or let them follow their role again.
''' The modules are in folders, one per menu group (File Maintenance, Reports ...), plus the Mobile App.
''' A closed folder shows how much of it is allowed; click it to open it. Clicking a folder's
''' Admin / Cashier (or Follow / Allow / Deny) cell sets the whole folder at once.
''' Changes are saved at once and reach every laptop within a few seconds.
''' </summary>
<System.ComponentModel.DesignerCategory("Code")>
Public Class PermissionsForm
    Inherits Form
    Implements ILiveForm

    Private Shared ReadOnly Green As Color = Color.FromArgb(22, 130, 60)
    Private Shared ReadOnly OffGrey As Color = Color.FromArgb(205, 205, 205)
    Private Shared ReadOnly FolderBack As Color = Color.FromArgb(249, 246, 246)
    Private Shared ReadOnly FolderLine As Color = Color.FromArgb(232, 222, 222)
    Private Shared ReadOnly FolderFont As Font = Theme.MakeFont("Segoe UI Semibold", 10.5F)

    ''' <summary>One folder: a menu group and its modules.</summary>
    Private NotInheritable Class Folder
        Public ReadOnly Name As String
        Public ReadOnly Modules As New List(Of ModuleInfo)()
        Public Sub New(name As String)
            Me.Name = name
        End Sub
        ''' <summary>The modules that can be given to someone (not Superadmin only).</summary>
        Public Function Editable() As List(Of ModuleInfo)
            Dim l As New List(Of ModuleInfo)()
            For Each m As ModuleInfo In Modules
                If Not Permissions.IsSuperadminOnly(m.Key) Then l.Add(m)
            Next
            Return l
        End Function
        Public ReadOnly Property IsMobileApp As Boolean
            Get
                Return Modules.Count = 1 AndAlso Modules(0).Key = Permissions.MobileAppKey
            End Get
        End Property
    End Class

    Private ReadOnly _header As New Panel()
    Private ReadOnly _title As New Label()
    Private ReadOnly _subtitle As New Label()

    Private ReadOnly _bar As New Panel()
    Private ReadOnly _tabs As New PillTabs()
    Private ReadOnly _status As New Label()
    Private ReadOnly _btnFolders As New MaroonButton()
    Private ReadOnly _btnDefaults As New MaroonButton()
    Private ReadOnly _btnClearUser As New MaroonButton()

    ' by role
    Private ReadOnly _rolePage As New Panel()
    Private ReadOnly _roleGrid As New DataGridView()

    ' by user
    Private ReadOnly _userPage As New Panel()
    Private ReadOnly _users As New DataGridView()
    Private ReadOnly _userTitle As New Label()
    Private ReadOnly _userHint As New Label()
    Private ReadOnly _userGrid As New DataGridView()

    Private ReadOnly _folders As List(Of Folder) = BuildFolders()
    Private ReadOnly _roleOpen As New HashSet(Of String)()
    Private ReadOnly _userOpen As New HashSet(Of String)()

    Private _roleRules As New Dictionary(Of String, Boolean)()
    Private _userRules As New Dictionary(Of String, Boolean)()
    Private _usersSig As String = ""
    Private _userId As Integer
    Private _userName As String = ""
    Private _userRole As String = ""

    Public Sub New()
        Me.AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi
        BackColor = Color.White
        Font = Theme.FontBase

        _header.Dock = DockStyle.Top
        _header.Height = Theme.S(78)
        _header.BackColor = Color.White
        _title.AutoSize = True
        _title.Font = Theme.FontTitle
        _title.ForeColor = Theme.Maroon
        _title.Text = "Permission Maintenance"
        _title.Location = New Point(Theme.S(28), Theme.S(10))
        _subtitle.AutoSize = True
        _subtitle.Font = Theme.FontBase
        _subtitle.ForeColor = Theme.TextMuted
        _subtitle.Text = "Click a folder to open it. Click a folder's switch to set the whole folder. A setting for one person wins over their role; the Superadmin can always open everything."
        _subtitle.Location = New Point(Theme.S(31), Theme.S(48))
        _header.Controls.AddRange(New Control() {_title, _subtitle})

        ' tabs + actions
        _bar.Dock = DockStyle.Top
        _bar.Height = Theme.S(56)
        _bar.BackColor = Color.White
        _tabs.SetItems("By role", "By user")
        _tabs.Location = New Point(Theme.S(28), Theme.S(6))
        AddHandler _tabs.SelectedIndexChanged, Sub(s, e) ShowPage()

        _status.AutoSize = False
        _status.Font = Theme.FontSmall
        _status.TextAlign = ContentAlignment.MiddleLeft

        _btnFolders.Text = "Open all folders"
        _btnFolders.MakeSecondary()
        _btnFolders.Size = New Size(Theme.S(170), Theme.S(40))
        AddHandler _btnFolders.Click, AddressOf OnOpenCloseAll

        _btnDefaults.Text = "Restore defaults"
        _btnDefaults.MakeSecondary()
        _btnDefaults.Size = New Size(Theme.S(170), Theme.S(40))
        AddHandler _btnDefaults.Click, AddressOf OnRestoreDefaults

        _btnClearUser.Text = "Follow role for everything"
        _btnClearUser.MakeSecondary()
        _btnClearUser.Size = New Size(Theme.S(230), Theme.S(40))
        AddHandler _btnClearUser.Click, AddressOf OnClearUser

        _bar.Controls.AddRange(New Control() {_tabs, _status, _btnFolders, _btnDefaults, _btnClearUser})
        AddHandler _bar.Resize, Sub(s, e) LayoutBar()

        BuildRolePage()
        BuildUserPage()

        Controls.Add(_rolePage)
        Controls.Add(_userPage)
        Controls.Add(_bar)
        Controls.Add(_header)
        ShowPage()
    End Sub

    ' ==================================================================== building
    ''' <summary>The menu groups in menu order, then the Mobile App.</summary>
    Private Shared Function BuildFolders() As List(Of Folder)
        Dim l As New List(Of Folder)()
        For Each g As String In ModuleCatalog.GroupOrder
            Dim f As New Folder(g)
            For Each m As ModuleInfo In ModuleCatalog.All
                If m.Group = g Then f.Modules.Add(m)
            Next
            If f.Modules.Count > 0 Then l.Add(f)
        Next
        Dim app As New Folder(ModuleCatalog.MobileApp.Title)
        app.Modules.Add(ModuleCatalog.MobileApp)
        l.Add(app)
        Return l
    End Function

    Private Shared Function Col(name As String, header As String, weight As Single) As DataGridViewTextBoxColumn
        Dim c As New DataGridViewTextBoxColumn()
        c.Name = name
        c.HeaderText = header
        c.FillWeight = weight
        c.SortMode = DataGridViewColumnSortMode.NotSortable
        Return c
    End Function

    ''' <summary>A folder row, then its module rows (indented, hidden while the folder is closed).</summary>
    Private Sub AddFolderRows(grid As DataGridView, moduleText As Func(Of ModuleInfo, Object()))
        For Each f As Folder In _folders
            Dim cells(grid.Columns.Count - 1) As Object
            cells(0) = f.Name
            Dim fi As Integer = grid.Rows.Add(cells)
            grid.Rows(fi).Tag = f
            grid.Rows(fi).Height = Theme.S(46)
            For Each m As ModuleInfo In f.Modules
                Dim i As Integer = grid.Rows.Add(moduleText(m))
                grid.Rows(i).Tag = m
                grid.Rows(i).Cells(0).Style.Padding = New Padding(Theme.S(46), 0, 0, 0)
                grid.Rows(i).Visible = False
            Next
        Next
    End Sub

    Private Sub BuildRolePage()
        _rolePage.Dock = DockStyle.Fill
        _rolePage.BackColor = Color.White
        _rolePage.Padding = New Padding(Theme.S(28), 0, Theme.S(28), Theme.S(18))

        GridStyle.Apply(_roleGrid, 42)
        NoSelectionLook(_roleGrid)
        _roleGrid.Dock = DockStyle.Fill
        _roleGrid.Columns.Add(Col("module", "FOLDER / MODULE", 26))
        _roleGrid.Columns.Add(Col("desc", "WHAT IT IS", 40))
        _roleGrid.Columns.Add(Col("admin", "ADMIN", 17))
        _roleGrid.Columns.Add(Col("cashier", "CASHIER", 17))
        AddFolderRows(_roleGrid, Function(m) New Object() {m.Title, m.Description, False, False})
        AddHandler _roleGrid.CellPainting, AddressOf OnRolePainting
        AddHandler _roleGrid.CellMouseClick, AddressOf OnRoleClick
        AddHandler _roleGrid.CellMouseEnter, AddressOf OnGridMouseEnter
        AddHandler _roleGrid.CellMouseLeave, Sub(s, e) _roleGrid.Cursor = Cursors.Default
        _rolePage.Controls.Add(_roleGrid)
    End Sub

    Private Sub BuildUserPage()
        _userPage.Dock = DockStyle.Fill
        _userPage.BackColor = Color.White

        GridStyle.Apply(_users, 46)
        _users.Columns.Add(Col("name", "PERSON", 62))
        _users.Columns.Add(Col("role", "ROLE", 38))
        AddHandler _users.SelectionChanged, Sub(s, e) OnUserSelected()

        _userTitle.AutoSize = False
        _userTitle.Font = Theme.FontHeading
        _userTitle.ForeColor = Theme.TextDark
        _userHint.AutoSize = False
        _userHint.Font = Theme.FontSmall
        _userHint.ForeColor = Theme.TextMuted

        GridStyle.Apply(_userGrid, 42)
        NoSelectionLook(_userGrid)
        _userGrid.Columns.Add(Col("module", "FOLDER / MODULE", 26))
        _userGrid.Columns.Add(Col("rolegives", "THE ROLE GIVES", 17))
        _userGrid.Columns.Add(Col("follow", "FOLLOW ROLE", 13))
        _userGrid.Columns.Add(Col("allow", "ALLOW", 10))
        _userGrid.Columns.Add(Col("deny", "DENY", 10))
        _userGrid.Columns.Add(Col("result", "RESULT", 17))
        AddFolderRows(_userGrid, Function(m) New Object() {m.Title, "", "", "", "", ""})
        AddHandler _userGrid.CellPainting, AddressOf OnUserPainting
        AddHandler _userGrid.CellMouseClick, AddressOf OnUserGridClick
        AddHandler _userGrid.CellMouseEnter, AddressOf OnGridMouseEnter
        AddHandler _userGrid.CellMouseLeave, Sub(s, e) _userGrid.Cursor = Cursors.Default

        _userPage.Controls.AddRange(New Control() {_users, _userTitle, _userHint, _userGrid})
        AddHandler _userPage.Resize, Sub(s, e) LayoutUserPage()
    End Sub

    ''' <summary>These lists are switches, not things to select: no highlighted row.</summary>
    Private Shared Sub NoSelectionLook(grid As DataGridView)
        grid.DefaultCellStyle.SelectionBackColor = Color.White
        grid.DefaultCellStyle.SelectionForeColor = Theme.TextDark
        AddHandler grid.SelectionChanged, Sub(s, e) grid.ClearSelection()
    End Sub

    ''' <summary>"Products and Delivery Management": the modules that follow the Mobile App switch.</summary>
    Private Shared Function LinkedTitles() As String
        Dim names As New List(Of String)()
        For Each k As String In Permissions.MobileLinked
            For Each m As ModuleInfo In ModuleCatalog.All
                If m.Key = k Then names.Add(m.Title)
            Next
        Next
        Return String.Join(" and ", names)
    End Function

    Private Shared Function Titles(mods As List(Of ModuleInfo)) As String
        Dim names As New List(Of String)()
        For Each m As ModuleInfo In mods
            names.Add(m.Title)
        Next
        Return String.Join(", ", names)
    End Function

    ' ==================================================================== folders open / closed
    Private Function OpenSet(grid As DataGridView) As HashSet(Of String)
        Return If(grid Is _roleGrid, _roleOpen, _userOpen)
    End Function

    Private Function CurrentGrid() As DataGridView
        Return If(_tabs.SelectedIndex = 0, _roleGrid, _userGrid)
    End Function

    ''' <summary>Shows the modules of the open folders and hides the others.</summary>
    Private Sub ApplyFolders(grid As DataGridView)
        Dim open As HashSet(Of String) = OpenSet(grid)
        grid.CurrentCell = Nothing          ' a hidden row cannot be the current one
        grid.SuspendLayout()
        Dim isOpen As Boolean = False
        For Each r As DataGridViewRow In grid.Rows
            Dim f As Folder = TryCast(r.Tag, Folder)
            If f IsNot Nothing Then
                isOpen = open.Contains(f.Name)
            ElseIf r.Visible <> isOpen Then
                r.Visible = isOpen
            End If
        Next
        grid.ResumeLayout()
        grid.Invalidate()
        UpdateFoldersButton()
    End Sub

    Private Sub ToggleFolder(grid As DataGridView, f As Folder)
        Dim open As HashSet(Of String) = OpenSet(grid)
        If Not open.Remove(f.Name) Then open.Add(f.Name)
        ApplyFolders(grid)
    End Sub

    Private Sub OpenFolder(grid As DataGridView, f As Folder)
        If OpenSet(grid).Add(f.Name) Then ApplyFolders(grid)
    End Sub

    Private Function AllOpen(grid As DataGridView) As Boolean
        Return OpenSet(grid).Count >= _folders.Count
    End Function

    Private Sub UpdateFoldersButton()
        _btnFolders.Text = If(AllOpen(CurrentGrid()), "Close all folders", "Open all folders")
    End Sub

    Private Sub OnOpenCloseAll(sender As Object, e As EventArgs)
        Dim grid As DataGridView = CurrentGrid()
        Dim open As HashSet(Of String) = OpenSet(grid)
        If AllOpen(grid) Then
            open.Clear()
        Else
            For Each f As Folder In _folders
                open.Add(f.Name)
            Next
        End If
        ApplyFolders(grid)
    End Sub

    ' ==================================================================== layout
    Private Sub LayoutBar()
        Dim right As Integer = _bar.ClientSize.Width - Theme.S(28)
        _btnDefaults.Location = New Point(right - _btnDefaults.Width, Theme.S(6))
        _btnClearUser.Location = New Point(right - _btnClearUser.Width, Theme.S(6))
        Dim actionLeft As Integer = If(_btnDefaults.Visible, _btnDefaults.Left, _btnClearUser.Left)
        _btnFolders.Location = New Point(actionLeft - Theme.S(10) - _btnFolders.Width, Theme.S(6))
        Dim statusLeft As Integer = _tabs.Right + Theme.S(20)
        Dim statusRight As Integer = _btnFolders.Left - Theme.S(16)
        _status.SetBounds(statusLeft, Theme.S(6), Math.Max(Theme.S(100), statusRight - statusLeft), Theme.S(40))
    End Sub

    Private Sub LayoutUserPage()
        Dim m As Integer = Theme.S(28)
        Dim h As Integer = _userPage.ClientSize.Height
        Dim w As Integer = _userPage.ClientSize.Width
        Dim listW As Integer = Math.Max(Theme.S(300), CInt((w - 2 * m) * 0.3))
        _users.SetBounds(m, 0, listW, Math.Max(Theme.S(100), h - Theme.S(18)))
        Dim x As Integer = m + listW + Theme.S(24)
        Dim rw As Integer = Math.Max(Theme.S(420), w - x - m)
        _userTitle.SetBounds(x, 0, rw, Theme.S(28))
        _userHint.SetBounds(x, Theme.S(28), rw, Theme.S(22))
        _userGrid.SetBounds(x, Theme.S(56), rw, Math.Max(Theme.S(100), h - Theme.S(56) - Theme.S(18)))
    End Sub

    Private Sub ShowPage()
        Dim byRole As Boolean = (_tabs.SelectedIndex = 0)
        _rolePage.Visible = byRole
        _userPage.Visible = Not byRole
        _btnDefaults.Visible = byRole
        _btnClearUser.Visible = Not byRole
        If byRole Then _rolePage.BringToFront() Else _userPage.BringToFront()
        LayoutBar()
        If Not byRole Then LayoutUserPage()
        UpdateClearButton()
        UpdateFoldersButton()
    End Sub

    ' ==================================================================== data
    Public ReadOnly Property WatchedTables As String() Implements ILiveForm.WatchedTables
        Get
            Return New String() {"role_permissions", "user_permissions", "users"}
        End Get
    End Property

    Public Sub RefreshData() Implements ILiveForm.RefreshData
        LoadAll()
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        LoadAll()
    End Sub

    Private Sub LoadAll()
        Try
            _roleRules = PermissionRepository.GetRoleRules()
            LoadUsers()
            If _userId > 0 Then _userRules = PermissionRepository.GetUserRules(_userId)
        Catch ex As Exception
            Say("Could not load the permissions: " & ex.Message, True)
            Return
        End Try
        FillRoleValues()
        FillUserValues()
    End Sub

    Private Function RoleValue(role As String, key As String) As Boolean
        If Permissions.IsSuperadminOnly(key) Then Return False
        Dim v As Boolean
        If _roleRules.TryGetValue(role & "|" & key, v) Then Return v
        Return Permissions.DefaultAccess(role, key)
    End Function

    ''' <summary>What this person ends up with for the module (their own setting, or else the role).</summary>
    Private Function UserResult(key As String) As Boolean
        If Permissions.IsSuperadminOnly(key) Then Return False
        Dim own As Boolean
        If _userRules.TryGetValue(key, own) Then Return own
        Return RoleValue(_userRole, key)
    End Function

    Private Function CountRole(f As Folder, role As String) As Integer
        Dim n As Integer = 0
        For Each m As ModuleInfo In f.Editable()
            If RoleValue(role, m.Key) Then n += 1
        Next
        Return n
    End Function

    Private Sub FillRoleValues()
        For Each r As DataGridViewRow In _roleGrid.Rows
            Dim m As ModuleInfo = TryCast(r.Tag, ModuleInfo)
            If m Is Nothing Then Continue For
            r.Cells("admin").Value = RoleValue("admin", m.Key)
            r.Cells("cashier").Value = RoleValue("cashier", m.Key)
        Next
        _roleGrid.Invalidate()
    End Sub

    Private Sub LoadUsers()
        Dim dt As DataTable = PermissionRepository.GetUsers()
        Dim sb As New StringBuilder()
        For Each row As DataRow In dt.Rows
            sb.Append(row("id")).Append(":"c).Append(row("full_name")).Append(":"c).Append(row("role")).Append(":"c).
               Append(row("is_active")).Append(":"c).Append(row("custom_count")).Append("|"c)
        Next
        Dim sig As String = sb.ToString()
        If sig = _usersSig Then Return
        _usersSig = sig

        Dim keep As Integer = _userId
        _users.Rows.Clear()
        For Each row As DataRow In dt.Rows
            Dim custom As Integer = Convert.ToInt32(row("custom_count"))
            Dim roleText As String = Permissions.RoleDisplay(row("role").ToString()) &
                                     If(custom > 0, "  +" & custom, "") &
                                     If(Convert.ToInt32(row("is_active")) = 0, "  (disabled)", "")
            Dim i As Integer = _users.Rows.Add(row("full_name").ToString(), roleText)
            _users.Rows(i).Tag = row
            If custom > 0 Then _users.Rows(i).Cells("role").Style.ForeColor = Theme.Maroon
        Next

        _users.ClearSelection()
        Dim picked As Boolean = False
        For Each r As DataGridViewRow In _users.Rows
            If Convert.ToInt32(DirectCast(r.Tag, DataRow)("id")) = keep Then
                r.Selected = True
                _users.CurrentCell = r.Cells(0)
                picked = True
                Exit For
            End If
        Next
        If Not picked AndAlso _users.Rows.Count > 0 Then
            _users.Rows(0).Selected = True
            _users.CurrentCell = _users.Rows(0).Cells(0)
        End If
        OnUserSelected()
    End Sub

    Private Sub OnUserSelected()
        Dim row As DataRow = Nothing
        If _users.SelectedRows.Count > 0 Then row = TryCast(_users.SelectedRows(0).Tag, DataRow)
        If row Is Nothing Then
            _userId = 0
            _userName = ""
            _userRole = ""
            _userRules = New Dictionary(Of String, Boolean)()
        Else
            Dim id As Integer = Convert.ToInt32(row("id"))
            _userName = row("full_name").ToString()
            _userRole = Permissions.NormalizeRole(row("role").ToString())
            If id <> _userId Then
                _userId = id
                Try
                    _userRules = PermissionRepository.GetUserRules(id)
                Catch ex As Exception
                    _userRules = New Dictionary(Of String, Boolean)()
                    Say("Could not load this person's permissions: " & ex.Message, True)
                End Try
                ' another person: open the folders where they have their own settings
                _userOpen.Clear()
                For Each f As Folder In _folders
                    For Each m As ModuleInfo In f.Modules
                        If _userRules.ContainsKey(m.Key) Then
                            _userOpen.Add(f.Name)
                            Exit For
                        End If
                    Next
                Next
                ApplyFolders(_userGrid)
            End If
        End If
        FillUserValues()
    End Sub

    Private Sub FillUserValues()
        If _userId = 0 Then
            _userTitle.Text = If(_users.Rows.Count = 0, "No admins or cashiers yet", "Choose a person on the left")
            _userHint.Text = "Add people in File Maintenance > Users."
            _userGrid.Visible = False
            UpdateClearButton()
            Return
        End If
        _userGrid.Visible = True
        _userTitle.Text = _userName & "  (" & Permissions.RoleDisplay(_userRole) & ")"
        _userHint.Text = If(_userRules.Count = 0,
                            "Follows the " & Permissions.RoleDisplay(_userRole) & " role for every module.",
                            "Own setting for " & _userRules.Count & If(_userRules.Count = 1, " module", " modules") &
                            " (their folders are open); the rest follow the " & Permissions.RoleDisplay(_userRole) & " role.")
        For Each r As DataGridViewRow In _userGrid.Rows
            Dim m As ModuleInfo = TryCast(r.Tag, ModuleInfo)
            If m Is Nothing Then Continue For
            If Permissions.IsSuperadminOnly(m.Key) Then
                r.Cells("rolegives").Value = "Superadmin only"
                r.Cells("result").Value = "Not allowed"
            Else
                r.Cells("rolegives").Value = If(RoleValue(_userRole, m.Key), "Allowed", "Not allowed")
                r.Cells("result").Value = If(UserResult(m.Key), "Allowed", "Not allowed")
            End If
        Next
        _userGrid.Invalidate()
        UpdateClearButton()
    End Sub

    Private Sub UpdateClearButton()
        _btnClearUser.Enabled = (_userId > 0 AndAlso _userRules.Count > 0)
    End Sub

    ''' <summary>-1 follow role, 1 allow, 0 deny</summary>
    Private Function UserChoice(key As String) As Integer
        Dim v As Boolean
        If _userRules.TryGetValue(key, v) Then Return If(v, 1, 0)
        Return -1
    End Function

    ' ==================================================================== painting
    Private Shared Sub DrawSwitch(g As Graphics, cell As Rectangle, isOn As Boolean, Optional label As String = Nothing,
                                  Optional isPartial As Boolean = False)
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim r As New Rectangle(cell.X + Theme.S(12), cell.Y + (cell.Height - Theme.S(22)) \ 2, Theme.S(42), Theme.S(22))
        Dim back As Color = If(isOn, Theme.Maroon, If(isPartial, Color.FromArgb(214, 160, 160), OffGrey))
        Using path As GraphicsPath = Theme.RoundedRect(r, r.Height \ 2)
            Using b As New SolidBrush(back)
                g.FillPath(b, path)
            End Using
        End Using
        Dim d As Integer = r.Height - Theme.S(6)
        Dim kx As Integer
        If isOn Then
            kx = r.Right - d - Theme.S(3)
        ElseIf isPartial Then
            kx = r.X + (r.Width - d) \ 2          ' knob in the middle: some on, some off
        Else
            kx = r.X + Theme.S(3)
        End If
        Using b As New SolidBrush(Color.White)
            g.FillEllipse(b, kx, r.Y + Theme.S(3), d, d)
        End Using
        Dim text As String = If(label, If(isOn, "Allowed", "Not allowed"))
        TextRenderer.DrawText(g, text, Theme.FontSmall,
                              New Rectangle(r.Right + Theme.S(10), cell.Y, Math.Max(0, cell.Right - r.Right - Theme.S(10)), cell.Height),
                              If(isOn OrElse isPartial, Theme.TextDark, Theme.TextMuted),
                              TextFormatFlags.Left Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis)
    End Sub

    ''' <summary>A radio button. For a folder: mixed = some of its modules have this choice.</summary>
    Private Shared Sub DrawRadio(g As Graphics, cell As Rectangle, selected As Boolean, accent As Color, Optional mixed As Boolean = False)
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim d As Integer = Theme.S(18)
        Dim r As New Rectangle(cell.X + Theme.S(14), cell.Y + (cell.Height - d) \ 2, d, d)
        Using p As New Pen(If(selected OrElse mixed, accent, Color.FromArgb(185, 185, 185)), 1.6F)
            g.DrawEllipse(p, r)
        End Using
        If selected Then
            Dim inner As New Rectangle(r.X + Theme.S(4), r.Y + Theme.S(4), d - Theme.S(8), d - Theme.S(8))
            Using b As New SolidBrush(accent)
                g.FillEllipse(b, inner)
            End Using
        ElseIf mixed Then
            Using p As New Pen(accent, 2.0F)
                Dim y As Integer = r.Y + d \ 2
                g.DrawLine(p, r.X + Theme.S(5), y, r.Right - Theme.S(5), y)
            End Using
        End If
    End Sub

    Private Shared Sub DrawMuted(g As Graphics, cell As Rectangle, text As String)
        TextRenderer.DrawText(g, text, Theme.FontSmall, New Rectangle(cell.X + Theme.S(12), cell.Y, cell.Width - Theme.S(12), cell.Height),
                              Color.FromArgb(150, 150, 150), TextFormatFlags.Left Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis)
    End Sub

    ''' <summary>The tinted band of a folder row (every cell of it).</summary>
    Private Shared Sub PaintFolderBack(e As DataGridViewCellPaintingEventArgs)
        Using b As New SolidBrush(FolderBack)
            e.Graphics.FillRectangle(b, e.CellBounds)
        End Using
        Using p As New Pen(FolderLine)
            e.Graphics.DrawLine(p, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1)
        End Using
    End Sub

    ''' <summary>Arrow, folder icon and the folder's name.</summary>
    Private Shared Sub DrawFolderTitle(g As Graphics, cell As Rectangle, f As Folder, isOpen As Boolean)
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim cy As Integer = cell.Y + cell.Height \ 2

        ' arrow: right when closed, down when open
        Dim ax As Integer = cell.X + Theme.S(14)
        Dim a As Integer = Theme.S(5)
        Dim pts As Point()
        If isOpen Then
            pts = {New Point(ax - a, cy - a \ 2), New Point(ax + a, cy - a \ 2), New Point(ax, cy + a)}
        Else
            pts = {New Point(ax - a \ 2, cy - a), New Point(ax + a, cy), New Point(ax - a \ 2, cy + a)}
        End If
        Using b As New SolidBrush(Theme.TextMuted)
            g.FillPolygon(b, pts)
        End Using

        ' folder: a tab and a body
        Dim fx As Integer = cell.X + Theme.S(28)
        Dim fw As Integer = Theme.S(22)
        Dim fh As Integer = Theme.S(16)
        Dim fy As Integer = cy - fh \ 2 + Theme.S(1)
        Dim tint As Color = If(isOpen, Theme.MaroonLight, Theme.Maroon)
        Using b As New SolidBrush(tint)
            Using tab As GraphicsPath = Theme.RoundedRect(New Rectangle(fx, fy - Theme.S(3), fw \ 2, Theme.S(6)), Theme.S(2))
                g.FillPath(b, tab)
            End Using
            Using body As GraphicsPath = Theme.RoundedRect(New Rectangle(fx, fy, fw, fh), Theme.S(3))
                g.FillPath(b, body)
            End Using
        End Using
        If isOpen Then
            Using p As New Pen(Color.FromArgb(150, Color.White), 1.4F)
                g.DrawLine(p, fx + Theme.S(3), fy + Theme.S(5), fx + fw - Theme.S(3), fy + Theme.S(5))
            End Using
        End If

        Dim tx As Integer = fx + fw + Theme.S(10)
        TextRenderer.DrawText(g, f.Name, FolderFont, New Rectangle(tx, cell.Y, Math.Max(0, cell.Right - tx), cell.Height),
                              Theme.TextDark, TextFormatFlags.Left Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis)
    End Sub

    Private Shared Function CountText(n As Integer, total As Integer) As String
        If total = 0 Then Return "Superadmin only"
        If n = total Then Return If(total = 1, "Allowed", "All " & total & " allowed")
        If n = 0 Then Return "None allowed"
        Return n & " of " & total & " allowed"
    End Function

    Private Sub OnRolePainting(sender As Object, e As DataGridViewCellPaintingEventArgs)
        If e.RowIndex < 0 OrElse e.ColumnIndex < 0 Then Return
        Dim name As String = _roleGrid.Columns(e.ColumnIndex).Name
        Dim row As DataGridViewRow = _roleGrid.Rows(e.RowIndex)

        Dim f As Folder = TryCast(row.Tag, Folder)
        If f IsNot Nothing Then
            PaintFolderBack(e)
            Dim editable As List(Of ModuleInfo) = f.Editable()
            Select Case name
                Case "module"
                    DrawFolderTitle(e.Graphics, e.CellBounds, f, _roleOpen.Contains(f.Name))
                Case "desc"
                    Dim hint As String = f.Modules.Count & If(f.Modules.Count = 1, " module", " modules")
                    If editable.Count < f.Modules.Count Then hint &= "  (" & (f.Modules.Count - editable.Count) & " Superadmin only)"
                    hint &= If(_roleOpen.Contains(f.Name), "", "   -   click to open")
                    DrawMuted(e.Graphics, e.CellBounds, hint)
                Case "admin", "cashier"
                    If editable.Count = 0 Then
                        DrawMuted(e.Graphics, e.CellBounds, "Superadmin only")
                    Else
                        Dim n As Integer = CountRole(f, name)
                        DrawSwitch(e.Graphics, e.CellBounds, n = editable.Count, CountText(n, editable.Count), n > 0 AndAlso n < editable.Count)
                    End If
            End Select
            e.Handled = True
            Return
        End If

        If name <> "admin" AndAlso name <> "cashier" Then Return
        e.Paint(e.CellBounds, DataGridViewPaintParts.Background Or DataGridViewPaintParts.Border Or DataGridViewPaintParts.SelectionBackground)
        Dim m As ModuleInfo = DirectCast(row.Tag, ModuleInfo)
        If Permissions.IsSuperadminOnly(m.Key) Then
            DrawMuted(e.Graphics, e.CellBounds, "Superadmin only")
        Else
            DrawSwitch(e.Graphics, e.CellBounds, e.Value IsNot Nothing AndAlso CBool(e.Value))
        End If
        e.Handled = True
    End Sub

    Private Sub OnUserPainting(sender As Object, e As DataGridViewCellPaintingEventArgs)
        If e.RowIndex < 0 OrElse e.ColumnIndex < 0 Then Return
        Dim name As String = _userGrid.Columns(e.ColumnIndex).Name
        Dim row As DataGridViewRow = _userGrid.Rows(e.RowIndex)

        Dim f As Folder = TryCast(row.Tag, Folder)
        If f IsNot Nothing Then
            PaintFolderBack(e)
            Dim editable As List(Of ModuleInfo) = f.Editable()
            Select Case name
                Case "module"
                    DrawFolderTitle(e.Graphics, e.CellBounds, f, _userOpen.Contains(f.Name))
                Case "rolegives"
                    If editable.Count = 0 Then
                        DrawMuted(e.Graphics, e.CellBounds, "Superadmin only")
                    Else
                        DrawMuted(e.Graphics, e.CellBounds, CountText(CountRole(f, _userRole), editable.Count))
                    End If
                Case "follow", "allow", "deny"
                    If editable.Count > 0 Then
                        Dim mine As Integer = If(name = "follow", -1, If(name = "allow", 1, 0))
                        Dim n As Integer = 0
                        For Each m As ModuleInfo In editable
                            If UserChoice(m.Key) = mine Then n += 1
                        Next
                        Dim accent As Color = If(name = "allow", Green, If(name = "deny", Theme.AlertRed, Theme.Maroon))
                        DrawRadio(e.Graphics, e.CellBounds, n = editable.Count, accent, n > 0 AndAlso n < editable.Count)
                    End If
                Case "result"
                    If editable.Count > 0 Then
                        Dim n As Integer = 0
                        For Each m As ModuleInfo In editable
                            If UserResult(m.Key) Then n += 1
                        Next
                        Pills.Draw(e.Graphics, e.CellBounds, CountText(n, editable.Count),
                                   If(n > 0, Color.FromArgb(226, 246, 234), Color.FromArgb(238, 238, 238)),
                                   If(n > 0, Green, Color.FromArgb(100, 100, 100)))
                    End If
            End Select
            e.Handled = True
            Return
        End If

        Dim mm As ModuleInfo = DirectCast(row.Tag, ModuleInfo)
        Select Case name
            Case "follow", "allow", "deny"
                e.Paint(e.CellBounds, DataGridViewPaintParts.Background Or DataGridViewPaintParts.Border Or DataGridViewPaintParts.SelectionBackground)
                If Permissions.IsSuperadminOnly(mm.Key) Then
                    If name = "follow" Then DrawMuted(e.Graphics, e.CellBounds, "-")
                Else
                    Dim choice As Integer = UserChoice(mm.Key)
                    Dim mine As Integer = If(name = "follow", -1, If(name = "allow", 1, 0))
                    Dim accent As Color = If(name = "allow", Green, If(name = "deny", Theme.AlertRed, Theme.Maroon))
                    DrawRadio(e.Graphics, e.CellBounds, choice = mine, accent)
                End If
                e.Handled = True
            Case "result"
                e.Paint(e.CellBounds, DataGridViewPaintParts.Background Or DataGridViewPaintParts.Border Or DataGridViewPaintParts.SelectionBackground)
                Dim text As String = If(e.Value, "").ToString()
                Dim allowed As Boolean = (text = "Allowed")
                Pills.Draw(e.Graphics, e.CellBounds, text,
                           If(allowed, Color.FromArgb(226, 246, 234), Color.FromArgb(238, 238, 238)),
                           If(allowed, Green, Color.FromArgb(100, 100, 100)))
                e.Handled = True
        End Select
    End Sub

    Private Sub OnGridMouseEnter(sender As Object, e As DataGridViewCellEventArgs)
        Dim g As DataGridView = DirectCast(sender, DataGridView)
        If e.RowIndex < 0 OrElse e.ColumnIndex < 0 Then Return
        Dim name As String = g.Columns(e.ColumnIndex).Name
        Dim f As Folder = TryCast(g.Rows(e.RowIndex).Tag, Folder)
        If f IsNot Nothing Then
            g.Cursor = Cursors.Hand     ' opens / closes, or sets the whole folder
            Return
        End If
        Dim m As ModuleInfo = DirectCast(g.Rows(e.RowIndex).Tag, ModuleInfo)
        Dim clickable As Boolean = Not Permissions.IsSuperadminOnly(m.Key) AndAlso
                                   (name = "admin" OrElse name = "cashier" OrElse name = "follow" OrElse name = "allow" OrElse name = "deny")
        g.Cursor = If(clickable, Cursors.Hand, Cursors.Default)
    End Sub

    ' ==================================================================== saving: by role
    Private Sub OnRoleClick(sender As Object, e As DataGridViewCellMouseEventArgs)
        If e.RowIndex < 0 OrElse e.ColumnIndex < 0 OrElse e.Button <> MouseButtons.Left Then Return
        Dim name As String = _roleGrid.Columns(e.ColumnIndex).Name
        Dim row As DataGridViewRow = _roleGrid.Rows(e.RowIndex)

        Dim f As Folder = TryCast(row.Tag, Folder)
        If f IsNot Nothing Then
            If name = "admin" OrElse name = "cashier" Then
                SetRoleFolder(f, name)
            Else
                ToggleFolder(_roleGrid, f)
            End If
            Return
        End If

        If name <> "admin" AndAlso name <> "cashier" Then Return
        SetRoleModule(DirectCast(row.Tag, ModuleInfo), name)
    End Sub

    Private Sub SetRoleModule(m As ModuleInfo, role As String)
        If Permissions.IsSuperadminOnly(m.Key) Then
            Say(m.Title & " is for the Superadmin only and cannot be given to a role.", True)
            Return
        End If

        Dim allow As Boolean = Not RoleValue(role, m.Key)
        Dim roleName As String = Permissions.RoleDisplay(role)
        Dim isApp As Boolean = (m.Key = Permissions.MobileAppKey)

        ' the Mobile App switch also moves Products and Delivery Management: say so before doing it
        If isApp Then
            Dim question As String
            If allow Then
                question = "Allow the Mobile App for every " & roleName & "?" & Environment.NewLine & Environment.NewLine &
                           "This also allows " & LinkedTitles() & " on the computers, for every " & roleName & "."
            Else
                question = "Take the Mobile App away from every " & roleName & "?" & Environment.NewLine & Environment.NewLine &
                           "This also removes " & LinkedTitles() & " from every " & roleName & " on the computers, " &
                           "and signs out their phones."
            End If
            question &= Environment.NewLine & Environment.NewLine & "A setting made for one person still wins over the role."
            If Not Confirm(Me, "Mobile App", question, MessageBoxIcon.Question) Then Return
        End If

        Try
            PermissionRepository.SetRole(role, m.Key, allow)
        Catch ex As Exception
            Say("Could not save: " & ex.Message, True)
            Return
        End Try

        _roleRules(role & "|" & m.Key) = allow
        If isApp Then
            For Each k As String In Permissions.MobileLinked
                _roleRules(role & "|" & k) = allow
            Next
        End If
        FillRoleValues()

        AuditLog.Write("PERMISSION", "Permission Maintenance",
                       roleName & ": " & m.Title & If(allow, " allowed", " not allowed") &
                       If(isApp, " (also " & LinkedTitles() & ")", ""))
        If isApp Then
            Say("Saved. " & If(allow, "Every " & roleName & " can now use the Mobile App, " & LinkedTitles() & ".",
                                      "A " & roleName & " can no longer use the Mobile App or open " & LinkedTitles() & "."), False)
        Else
            Say("Saved. " & If(allow, "Every " & roleName & " can now open " & m.Title & " (unless set to Deny for them).",
                                      "A " & roleName & " can no longer open " & m.Title & " (unless set to Allow for them)."), False)
        End If
        FillUserValues()
    End Sub

    ''' <summary>The whole folder for a role: all on, or (when all are on already) all off.</summary>
    Private Sub SetRoleFolder(f As Folder, role As String)
        Dim editable As List(Of ModuleInfo) = f.Editable()
        If editable.Count = 0 Then
            Say(f.Name & " is for the Superadmin only.", True)
            Return
        End If
        If f.IsMobileApp Then
            SetRoleModule(f.Modules(0), role)       ' one switch, with its own question
            Return
        End If

        Dim roleName As String = Permissions.RoleDisplay(role)
        Dim allow As Boolean = CountRole(f, role) < editable.Count
        Dim question As String =
            If(allow, "Allow every " & roleName & " to open all of " & f.Name & "?",
                      "Take all of " & f.Name & " away from every " & roleName & "?") &
            Environment.NewLine & Environment.NewLine & Titles(editable) &
            Environment.NewLine & Environment.NewLine & "A setting made for one person still wins over the role."
        If Not Confirm(Me, f.Name, question, MessageBoxIcon.Question) Then Return

        Try
            For Each m As ModuleInfo In editable
                If RoleValue(role, m.Key) <> allow Then PermissionRepository.SetRole(role, m.Key, allow)
                _roleRules(role & "|" & m.Key) = allow
            Next
        Catch ex As Exception
            Say("Could not save all of them: " & ex.Message, True)
            LoadAll()
            Return
        End Try

        FillRoleValues()
        FillUserValues()
        OpenFolder(_roleGrid, f)
        AuditLog.Write("PERMISSION", "Permission Maintenance",
                       roleName & ": all of " & f.Name & If(allow, " allowed", " not allowed") & " (" & Titles(editable) & ")")
        Say("Saved. " & If(allow, "Every " & roleName & " can now open all of " & f.Name & ".",
                                  "A " & roleName & " can no longer open anything in " & f.Name & ".") &
            " Settings for one person still win.", False)
    End Sub

    ' ==================================================================== saving: by user
    Private Sub OnUserGridClick(sender As Object, e As DataGridViewCellMouseEventArgs)
        If e.RowIndex < 0 OrElse e.ColumnIndex < 0 OrElse e.Button <> MouseButtons.Left OrElse _userId = 0 Then Return
        Dim name As String = _userGrid.Columns(e.ColumnIndex).Name
        Dim row As DataGridViewRow = _userGrid.Rows(e.RowIndex)

        Dim value As Boolean? = Nothing
        If name = "allow" Then value = True
        If name = "deny" Then value = False
        Dim isChoice As Boolean = (name = "follow" OrElse name = "allow" OrElse name = "deny")

        Dim f As Folder = TryCast(row.Tag, Folder)
        If f IsNot Nothing Then
            If isChoice Then
                SetUserFolder(f, value)
            Else
                ToggleFolder(_userGrid, f)
            End If
            Return
        End If

        If Not isChoice Then Return
        SetUserModule(DirectCast(row.Tag, ModuleInfo), value)
    End Sub

    Private Sub SetUserModule(m As ModuleInfo, value As Boolean?)
        If Permissions.IsSuperadminOnly(m.Key) Then
            Say(m.Title & " is for the Superadmin only and cannot be given to anyone else.", True)
            Return
        End If

        Dim before As Integer = UserChoice(m.Key)
        Dim after As Integer = If(value.HasValue, If(value.Value, 1, 0), -1)
        If before = after Then Return

        Dim isApp As Boolean = (m.Key = Permissions.MobileAppKey)

        ' the Mobile App switch also moves Products and Delivery Management: say so before doing it
        If isApp Then
            Dim question As String
            If Not value.HasValue Then
                question = "Make " & _userName & " follow the " & Permissions.RoleDisplay(_userRole) & " role for the Mobile App?" &
                           Environment.NewLine & Environment.NewLine &
                           LinkedTitles() & " follow the role too. If the role does not give the Mobile App, their phone is signed out."
            ElseIf value.Value Then
                question = "Give " & _userName & " the Mobile App?" & Environment.NewLine & Environment.NewLine &
                           "This also allows " & LinkedTitles() & " for them on the computers."
            Else
                question = "Take the Mobile App away from " & _userName & "?" & Environment.NewLine & Environment.NewLine &
                           "This also removes " & LinkedTitles() & " for them on the computers, and signs out their phone."
            End If
            If Not Confirm(Me, "Mobile App", question, MessageBoxIcon.Question) Then Return
        End If

        Try
            If Not PermissionRepository.SetUser(_userId, m.Key, value) Then
                Say("This person's permissions cannot be changed.", True)
                Return
            End If
        Catch ex As Exception
            Say("Could not save: " & ex.Message, True)
            Return
        End Try

        Dim keys As New List(Of String)()
        keys.Add(m.Key)
        If isApp Then keys.AddRange(Permissions.MobileLinked)
        For Each k As String In keys
            If value.HasValue Then
                _userRules(k) = value.Value
            Else
                _userRules.Remove(k)
            End If
        Next

        AuditLog.Write("PERMISSION", "Permission Maintenance",
                       _userName & ": " & m.Title & " " & ChoiceText(value) & If(isApp, " (also " & LinkedTitles() & ")", ""))
        Say("Saved. " & _userName & ": " & m.Title & " " & ChoiceText(value) & If(isApp, " (and " & LinkedTitles() & ")", "") & ".", False)
        AfterUserChange()
    End Sub

    ''' <summary>The whole folder for this person: Follow role, Allow or Deny for each module in it.</summary>
    Private Sub SetUserFolder(f As Folder, value As Boolean?)
        Dim editable As List(Of ModuleInfo) = f.Editable()
        If editable.Count = 0 Then
            Say(f.Name & " is for the Superadmin only.", True)
            Return
        End If
        If f.IsMobileApp Then
            SetUserModule(f.Modules(0), value)
            Return
        End If

        Dim after As Integer = If(value.HasValue, If(value.Value, 1, 0), -1)
        Dim changing As New List(Of ModuleInfo)()
        For Each m As ModuleInfo In editable
            If UserChoice(m.Key) <> after Then changing.Add(m)
        Next
        If changing.Count = 0 Then Return

        Dim question As String
        If Not value.HasValue Then
            question = "Make " & _userName & " follow the " & Permissions.RoleDisplay(_userRole) & " role for all of " & f.Name & "?"
        ElseIf value.Value Then
            question = "Allow " & _userName & " to open all of " & f.Name & ", whatever the role gives?"
        Else
            question = "Deny " & _userName & " all of " & f.Name & ", whatever the role gives?"
        End If
        question &= Environment.NewLine & Environment.NewLine & Titles(changing)
        If Not Confirm(Me, f.Name, question, MessageBoxIcon.Question) Then Return

        Try
            For Each m As ModuleInfo In changing
                If Not PermissionRepository.SetUser(_userId, m.Key, value) Then
                    Say("This person's permissions cannot be changed.", True)
                    Return
                End If
                If value.HasValue Then
                    _userRules(m.Key) = value.Value
                Else
                    _userRules.Remove(m.Key)
                End If
            Next
        Catch ex As Exception
            Say("Could not save all of them: " & ex.Message, True)
            Try
                _userRules = PermissionRepository.GetUserRules(_userId)   ' show what was really saved
            Catch
            End Try
            AfterUserChange()
            Return
        End Try

        OpenFolder(_userGrid, f)
        AuditLog.Write("PERMISSION", "Permission Maintenance",
                       _userName & ": all of " & f.Name & " " & ChoiceText(value) & " (" & Titles(changing) & ")")
        Say("Saved. " & _userName & ": all of " & f.Name & " " & ChoiceText(value) & ".", False)
        AfterUserChange()
    End Sub

    Private Function ChoiceText(value As Boolean?) As String
        If Not value.HasValue Then Return "follows the " & Permissions.RoleDisplay(_userRole) & " role"
        Return If(value.Value, "allowed", "not allowed")
    End Function

    Private Sub AfterUserChange()
        FillUserValues()
        _usersSig = ""      ' the +N count in the list changes
        Try
            LoadUsers()
        Catch
            ' ignore
        End Try
    End Sub

    ' ==================================================================== buttons
    Private Sub OnRestoreDefaults(sender As Object, e As EventArgs)
        If Not Confirm(Me, "Restore defaults",
                       "Put Admin and Cashier back to the original settings?" & Environment.NewLine & Environment.NewLine &
                       "Admin: everything except the Settings and User Monitoring, and the Mobile App." & Environment.NewLine &
                       "Cashier: POS, Sales Report and Text Size, and no Mobile App." & Environment.NewLine & Environment.NewLine &
                       "Settings made for one person are kept.", MessageBoxIcon.Question) Then Return
        Try
            PermissionRepository.RestoreRoleDefaults()
        Catch ex As Exception
            Say("Could not restore the defaults: " & ex.Message, True)
            Return
        End Try
        AuditLog.Write("PERMISSION", "Permission Maintenance", "Role permissions restored to the defaults")
        LoadAll()
        Say("Admin and Cashier are back to the original settings.", False)
    End Sub

    Private Sub OnClearUser(sender As Object, e As EventArgs)
        If _userId = 0 Then Return
        If Not Confirm(Me, "Follow role for everything",
                       "Remove the " & _userRules.Count & " own setting(s) of " & _userName & "?" & Environment.NewLine &
                       "They will follow the " & Permissions.RoleDisplay(_userRole) & " role for every module.",
                       MessageBoxIcon.Question) Then Return
        Try
            PermissionRepository.ClearUser(_userId)
        Catch ex As Exception
            Say("Could not save: " & ex.Message, True)
            Return
        End Try
        AuditLog.Write("PERMISSION", "Permission Maintenance", _userName & ": own settings removed (follows the role)")
        _userRules.Clear()
        _usersSig = ""
        LoadAll()
        Say("Saved. " & _userName & " follows the " & Permissions.RoleDisplay(_userRole) & " role again.", False)
    End Sub

    Private Sub Say(text As String, isError As Boolean)
        _status.ForeColor = If(isError, Theme.AlertRed, Green)
        _status.Text = text
    End Sub

End Class
