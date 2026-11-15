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
''' Changes are saved at once and reach every laptop within a few seconds.
''' </summary>
<System.ComponentModel.DesignerCategory("Code")>
Public Class PermissionsForm
    Inherits Form
    Implements ILiveForm

    Private Shared ReadOnly Green As Color = Color.FromArgb(22, 130, 60)
    Private Shared ReadOnly OffGrey As Color = Color.FromArgb(205, 205, 205)

    Private ReadOnly _header As New Panel()
    Private ReadOnly _title As New Label()
    Private ReadOnly _subtitle As New Label()

    Private ReadOnly _bar As New Panel()
    Private ReadOnly _tabs As New PillTabs()
    Private ReadOnly _status As New Label()
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
        _subtitle.Text = "Who can open each module. A setting for one person wins over their role. The Superadmin can always open everything."
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

        _btnDefaults.Text = "Restore defaults"
        _btnDefaults.MakeSecondary()
        _btnDefaults.Size = New Size(Theme.S(170), Theme.S(40))
        AddHandler _btnDefaults.Click, AddressOf OnRestoreDefaults

        _btnClearUser.Text = "Follow role for everything"
        _btnClearUser.MakeSecondary()
        _btnClearUser.Size = New Size(Theme.S(230), Theme.S(40))
        AddHandler _btnClearUser.Click, AddressOf OnClearUser

        _bar.Controls.AddRange(New Control() {_tabs, _status, _btnDefaults, _btnClearUser})
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
    Private Shared Function Col(name As String, header As String, weight As Single) As DataGridViewTextBoxColumn
        Dim c As New DataGridViewTextBoxColumn()
        c.Name = name
        c.HeaderText = header
        c.FillWeight = weight
        c.SortMode = DataGridViewColumnSortMode.NotSortable
        Return c
    End Function

    Private Sub BuildRolePage()
        _rolePage.Dock = DockStyle.Fill
        _rolePage.BackColor = Color.White
        _rolePage.Padding = New Padding(Theme.S(28), 0, Theme.S(28), Theme.S(18))

        GridStyle.Apply(_roleGrid, 42)
        NoSelectionLook(_roleGrid)
        _roleGrid.Dock = DockStyle.Fill
        _roleGrid.Columns.Add(Col("group", "GROUP", 14))
        _roleGrid.Columns.Add(Col("module", "MODULE", 20))
        _roleGrid.Columns.Add(Col("desc", "WHAT IT IS", 36))
        _roleGrid.Columns.Add(Col("admin", "ADMIN", 15))
        _roleGrid.Columns.Add(Col("cashier", "CASHIER", 15))
        For Each m As ModuleInfo In ModulesInOrder()
            Dim i As Integer = _roleGrid.Rows.Add(m.Group, m.Title, m.Description, False, False)
            _roleGrid.Rows(i).Tag = m
        Next
        MarkGroupStarts(_roleGrid)
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
        _userGrid.Columns.Add(Col("module", "MODULE", 26))
        _userGrid.Columns.Add(Col("rolegives", "THE ROLE GIVES", 17))
        _userGrid.Columns.Add(Col("follow", "FOLLOW ROLE", 13))
        _userGrid.Columns.Add(Col("allow", "ALLOW", 10))
        _userGrid.Columns.Add(Col("deny", "DENY", 10))
        _userGrid.Columns.Add(Col("result", "RESULT", 17))
        For Each m As ModuleInfo In ModulesInOrder()
            Dim i As Integer = _userGrid.Rows.Add(m.Title, "", "", "", "", "")
            _userGrid.Rows(i).Tag = m
        Next
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

    ''' <summary>Modules in menu order (group by group).</summary>
    Private Shared Function ModulesInOrder() As List(Of ModuleInfo)
        Dim l As New List(Of ModuleInfo)()
        For Each g As String In ModuleCatalog.GroupOrder
            For Each m As ModuleInfo In ModuleCatalog.All
                If m.Group = g Then l.Add(m)
            Next
        Next
        Return l
    End Function

    ''' <summary>The group name only on the first row of each group (easier to read).</summary>
    Private Shared Sub MarkGroupStarts(grid As DataGridView)
        Dim last As String = ""
        For Each r As DataGridViewRow In grid.Rows
            Dim g As String = DirectCast(r.Tag, ModuleInfo).Group
            r.Cells("group").Value = If(g = last, "", g)
            r.Cells("group").Style.ForeColor = Theme.TextMuted
            last = g
        Next
    End Sub

    ' ==================================================================== layout
    Private Sub LayoutBar()
        Dim right As Integer = _bar.ClientSize.Width - Theme.S(28)
        _btnDefaults.Location = New Point(right - _btnDefaults.Width, Theme.S(6))
        _btnClearUser.Location = New Point(right - _btnClearUser.Width, Theme.S(6))
        Dim statusLeft As Integer = _tabs.Right + Theme.S(20)
        Dim statusRight As Integer = Math.Min(_btnDefaults.Left, _btnClearUser.Left) - Theme.S(16)
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

    Private Sub FillRoleValues()
        For Each r As DataGridViewRow In _roleGrid.Rows
            Dim key As String = DirectCast(r.Tag, ModuleInfo).Key
            r.Cells("admin").Value = RoleValue("admin", key)
            r.Cells("cashier").Value = RoleValue("cashier", key)
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
                            "; the rest follow the " & Permissions.RoleDisplay(_userRole) & " role.")
        For Each r As DataGridViewRow In _userGrid.Rows
            Dim key As String = DirectCast(r.Tag, ModuleInfo).Key
            If Permissions.IsSuperadminOnly(key) Then
                r.Cells("rolegives").Value = "Superadmin only"
                r.Cells("result").Value = "Not allowed"
            Else
                Dim byRole As Boolean = RoleValue(_userRole, key)
                Dim own As Boolean
                Dim hasOwn As Boolean = _userRules.TryGetValue(key, own)
                r.Cells("rolegives").Value = If(byRole, "Allowed", "Not allowed")
                r.Cells("result").Value = If(If(hasOwn, own, byRole), "Allowed", "Not allowed")
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
    Private Shared Sub DrawSwitch(g As Graphics, cell As Rectangle, isOn As Boolean)
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim r As New Rectangle(cell.X + Theme.S(12), cell.Y + (cell.Height - Theme.S(22)) \ 2, Theme.S(42), Theme.S(22))
        Using path As GraphicsPath = Theme.RoundedRect(r, r.Height \ 2)
            Using b As New SolidBrush(If(isOn, Theme.Maroon, OffGrey))
                g.FillPath(b, path)
            End Using
        End Using
        Dim d As Integer = r.Height - Theme.S(6)
        Dim kx As Integer = If(isOn, r.Right - d - Theme.S(3), r.X + Theme.S(3))
        Using b As New SolidBrush(Color.White)
            g.FillEllipse(b, kx, r.Y + Theme.S(3), d, d)
        End Using
        TextRenderer.DrawText(g, If(isOn, "Allowed", "Not allowed"), Theme.FontSmall,
                              New Rectangle(r.Right + Theme.S(10), cell.Y, Math.Max(0, cell.Right - r.Right - Theme.S(10)), cell.Height),
                              If(isOn, Theme.TextDark, Theme.TextMuted),
                              TextFormatFlags.Left Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis)
    End Sub

    Private Shared Sub DrawRadio(g As Graphics, cell As Rectangle, selected As Boolean, accent As Color)
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim d As Integer = Theme.S(18)
        Dim r As New Rectangle(cell.X + Theme.S(14), cell.Y + (cell.Height - d) \ 2, d, d)
        Using p As New Pen(If(selected, accent, Color.FromArgb(185, 185, 185)), 1.6F)
            g.DrawEllipse(p, r)
        End Using
        If selected Then
            Dim inner As New Rectangle(r.X + Theme.S(4), r.Y + Theme.S(4), d - Theme.S(8), d - Theme.S(8))
            Using b As New SolidBrush(accent)
                g.FillEllipse(b, inner)
            End Using
        End If
    End Sub

    Private Shared Sub DrawMuted(g As Graphics, cell As Rectangle, text As String)
        TextRenderer.DrawText(g, text, Theme.FontSmall, New Rectangle(cell.X + Theme.S(12), cell.Y, cell.Width - Theme.S(12), cell.Height),
                              Color.FromArgb(150, 150, 150), TextFormatFlags.Left Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis)
    End Sub

    Private Sub OnRolePainting(sender As Object, e As DataGridViewCellPaintingEventArgs)
        If e.RowIndex < 0 OrElse e.ColumnIndex < 0 Then Return
        Dim name As String = _roleGrid.Columns(e.ColumnIndex).Name
        If name <> "admin" AndAlso name <> "cashier" Then Return
        e.Paint(e.CellBounds, DataGridViewPaintParts.Background Or DataGridViewPaintParts.Border Or DataGridViewPaintParts.SelectionBackground)
        Dim m As ModuleInfo = DirectCast(_roleGrid.Rows(e.RowIndex).Tag, ModuleInfo)
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
        Dim m As ModuleInfo = DirectCast(_userGrid.Rows(e.RowIndex).Tag, ModuleInfo)
        Select Case name
            Case "follow", "allow", "deny"
                e.Paint(e.CellBounds, DataGridViewPaintParts.Background Or DataGridViewPaintParts.Border Or DataGridViewPaintParts.SelectionBackground)
                If Permissions.IsSuperadminOnly(m.Key) Then
                    If name = "follow" Then DrawMuted(e.Graphics, e.CellBounds, "-")
                Else
                    Dim choice As Integer = UserChoice(m.Key)
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
        Dim m As ModuleInfo = DirectCast(g.Rows(e.RowIndex).Tag, ModuleInfo)
        Dim clickable As Boolean = Not Permissions.IsSuperadminOnly(m.Key) AndAlso
                                   (name = "admin" OrElse name = "cashier" OrElse name = "follow" OrElse name = "allow" OrElse name = "deny")
        g.Cursor = If(clickable, Cursors.Hand, Cursors.Default)
    End Sub

    ' ==================================================================== saving
    Private Sub OnRoleClick(sender As Object, e As DataGridViewCellMouseEventArgs)
        If e.RowIndex < 0 OrElse e.ColumnIndex < 0 OrElse e.Button <> MouseButtons.Left Then Return
        Dim name As String = _roleGrid.Columns(e.ColumnIndex).Name
        If name <> "admin" AndAlso name <> "cashier" Then Return
        Dim m As ModuleInfo = DirectCast(_roleGrid.Rows(e.RowIndex).Tag, ModuleInfo)
        If Permissions.IsSuperadminOnly(m.Key) Then
            Say(m.Title & " is for the Superadmin only and cannot be given to a role.", True)
            Return
        End If

        Dim cell As DataGridViewCell = _roleGrid.Rows(e.RowIndex).Cells(name)
        Dim allow As Boolean = Not CBool(cell.Value)
        Try
            PermissionRepository.SetRole(name, m.Key, allow)
        Catch ex As Exception
            Say("Could not save: " & ex.Message, True)
            Return
        End Try
        _roleRules(name & "|" & m.Key) = allow
        cell.Value = allow
        _roleGrid.InvalidateCell(cell)
        Dim roleName As String = Permissions.RoleDisplay(name)
        AuditLog.Write("PERMISSION", "Permission Maintenance", roleName & ": " & m.Title & If(allow, " allowed", " not allowed"))
        Say("Saved. " & If(allow, "Every " & roleName & " can now open " & m.Title & " (unless set to Deny for them).",
                                  "A " & roleName & " can no longer open " & m.Title & " (unless set to Allow for them)."), False)
        FillUserValues()
    End Sub

    Private Sub OnUserGridClick(sender As Object, e As DataGridViewCellMouseEventArgs)
        If e.RowIndex < 0 OrElse e.ColumnIndex < 0 OrElse e.Button <> MouseButtons.Left OrElse _userId = 0 Then Return
        Dim name As String = _userGrid.Columns(e.ColumnIndex).Name
        If name <> "follow" AndAlso name <> "allow" AndAlso name <> "deny" Then Return
        Dim m As ModuleInfo = DirectCast(_userGrid.Rows(e.RowIndex).Tag, ModuleInfo)
        If Permissions.IsSuperadminOnly(m.Key) Then
            Say(m.Title & " is for the Superadmin only and cannot be given to anyone else.", True)
            Return
        End If

        Dim value As Boolean? = Nothing
        If name = "allow" Then value = True
        If name = "deny" Then value = False
        Dim before As Integer = UserChoice(m.Key)
        Dim after As Integer = If(value.HasValue, If(value.Value, 1, 0), -1)
        If before = after Then Return

        Try
            If Not PermissionRepository.SetUser(_userId, m.Key, value) Then
                Say("This person's permissions cannot be changed.", True)
                Return
            End If
        Catch ex As Exception
            Say("Could not save: " & ex.Message, True)
            Return
        End Try

        If value.HasValue Then
            _userRules(m.Key) = value.Value
        Else
            _userRules.Remove(m.Key)
        End If
        Dim what As String = If(Not value.HasValue, "follows the " & Permissions.RoleDisplay(_userRole) & " role",
                                If(value.Value, "allowed", "not allowed"))
        AuditLog.Write("PERMISSION", "Permission Maintenance", _userName & ": " & m.Title & " " & what)
        Say("Saved. " & _userName & ": " & m.Title & " " & what & ".", False)
        FillUserValues()
        _usersSig = ""      ' the +N count in the list changes
        Try
            LoadUsers()
        Catch
            ' ignore
        End Try
    End Sub

    Private Sub OnRestoreDefaults(sender As Object, e As EventArgs)
        If Not Confirm(Me, "Restore defaults",
                       "Put Admin and Cashier back to the original settings?" & Environment.NewLine & Environment.NewLine &
                       "Admin: everything except the Settings and User Monitoring." & Environment.NewLine &
                       "Cashier: POS, Sales Report and Text Size." & Environment.NewLine & Environment.NewLine &
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
