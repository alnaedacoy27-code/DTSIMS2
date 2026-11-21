Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Data
Imports System.Text

''' <summary>
''' Who may open which module.
'''   1. The Superadmin can open everything, always.
'''   2. Database Settings and Permission Maintenance are for the Superadmin only (they can never be granted).
'''   3. A per-user setting (user_permissions) wins over the role:  Allow / Deny / follow the role.
'''   4. Otherwise the role setting (role_permissions) decides.
'''   5. When a role row is missing (or the tables cannot be read), the hard-coded defaults below are used,
'''      so a damaged table can never lock anyone out.
''' Settings > Permission Maintenance edits the tables; every laptop reloads them through LiveSync.
''' </summary>
Public Module Permissions

    ''' <summary>Only the Superadmin can ever open these.</summary>
    Public ReadOnly SuperadminOnly As String() = {"database_settings", "permissions", "audit_trail"}

    ''' <summary>The permission that lets a person sign in to the phone app (see MobileLinked).</summary>
    Public Const MobileAppKey As String = "mobile_app"

    ''' <summary>
    ''' The modules the phone app is built on. They follow the Mobile App switch: giving a person (or a role) the
    ''' Mobile App also gives these, and taking it away removes them, so nobody can sign in to an app that does nothing.
    ''' </summary>
    Public ReadOnly MobileLinked As String() = {"products", "delivery"}

    ''' <summary>The roles whose access can be changed.</summary>
    Public ReadOnly EditableRoles As String() = {"admin", "cashier"}

    ' factory defaults
    Private ReadOnly AdminDenied As String() = {"vat", "discounts", "stock_threshold", "database_settings", "user_monitoring", "permissions", "audit_trail",
                                                "payment_methods"}
    Private ReadOnly CashierAllowed As String() = {"pos", "sales_report", "text_size"}

    Private ReadOnly _lock As New Object()
    Private _roleRules As New Dictionary(Of String, Boolean)(StringComparer.Ordinal)   ' "cashier|inventory" -> allowed
    Private _userRules As New Dictionary(Of String, Boolean)(StringComparer.Ordinal)   ' logged-in user: "inventory" -> allowed

    Public Function NormalizeRole(role As String) As String
        If role Is Nothing Then Return ""
        Return role.Trim().ToLowerInvariant()
    End Function

    Public Function IsSuperadminOnly(moduleKey As String) As Boolean
        Return Array.IndexOf(SuperadminOnly, moduleKey) >= 0
    End Function

    ''' <summary>The factory setting for a role (used for "Restore defaults" and when a row is missing).</summary>
    Public Function DefaultAccess(role As String, moduleKey As String) As Boolean
        Select Case NormalizeRole(role)
            Case "superadmin"
                Return True
            Case "admin"
                Return Array.IndexOf(AdminDenied, moduleKey) < 0
            Case "cashier"
                Return Array.IndexOf(CashierAllowed, moduleKey) >= 0
            Case Else
                Return False
        End Select
    End Function

    ''' <summary>What the role gives, before any per-user setting.</summary>
    Public Function RoleAccess(role As String, moduleKey As String) As Boolean
        Dim r As String = NormalizeRole(role)
        If r = "superadmin" Then Return True
        If r <> "admin" AndAlso r <> "cashier" Then Return False
        If IsSuperadminOnly(moduleKey) Then Return False
        SyncLock _lock
            Dim v As Boolean
            If _roleRules.TryGetValue(r & "|" & moduleKey, v) Then Return v
        End SyncLock
        Return DefaultAccess(r, moduleKey)
    End Function

    ''' <summary>
    ''' Can the LOGGED-IN person (whose role is <paramref name="role"/>) open this module?
    ''' Includes their own per-user Allow / Deny.
    ''' </summary>
    Public Function CanAccess(role As String, moduleKey As String) As Boolean
        Dim r As String = NormalizeRole(role)
        If r = "superadmin" Then Return True
        If r <> "admin" AndAlso r <> "cashier" Then Return False
        If IsSuperadminOnly(moduleKey) Then Return False
        SyncLock _lock
            Dim v As Boolean
            If _userRules.TryGetValue(moduleKey, v) Then Return v
        End SyncLock
        Return RoleAccess(r, moduleKey)
    End Function

    ''' <summary>Reads role_permissions and the logged-in user's user_permissions. Keeps the old rules if the database cannot be read.</summary>
    Public Sub Reload()
        Dim roles As New Dictionary(Of String, Boolean)(StringComparer.Ordinal)
        Dim users As New Dictionary(Of String, Boolean)(StringComparer.Ordinal)
        Try
            For Each row As DataRow In Db.Table("SELECT role, module_key, is_allowed FROM role_permissions").Rows
                roles(NormalizeRole(row("role").ToString()) & "|" & row("module_key").ToString()) = Convert.ToInt32(row("is_allowed")) <> 0
            Next
            If AppSession.UserId > 0 Then
                For Each row As DataRow In Db.Table("SELECT module_key, is_allowed FROM user_permissions WHERE user_id = @id",
                                                    Db.Params("@id", AppSession.UserId)).Rows
                    users(row("module_key").ToString()) = Convert.ToInt32(row("is_allowed")) <> 0
                Next
            End If
        Catch
            Return
        End Try
        SyncLock _lock
            _roleRules = roles
            _userRules = users
        End SyncLock
    End Sub

    ''' <summary>Forget the per-user rules (log out).</summary>
    Public Sub ClearUser()
        SyncLock _lock
            _userRules = New Dictionary(Of String, Boolean)(StringComparer.Ordinal)
        End SyncLock
    End Sub

    ''' <summary>The modules the logged-in person may open, as one text (to notice when it changes).</summary>
    Public Function Signature(role As String) As String
        Dim sb As New StringBuilder()
        For Each m As ModuleInfo In ModuleCatalog.All
            If CanAccess(role, m.Key) Then sb.Append(m.Key).Append(","c)
        Next
        Return sb.ToString()
    End Function

    Public Function RoleDisplay(role As String) As String
        Select Case NormalizeRole(role)
            Case "superadmin"
                Return "Superadmin"
            Case "admin"
                Return "Admin"
            Case "cashier"
                Return "Cashier"
            Case Else
                Return "No role"
        End Select
    End Function

End Module

''' <summary>All SQL for role_permissions and user_permissions (Settings > Permission Maintenance).</summary>
Public Module PermissionRepository

    ''' <summary>"admin|pos" -> allowed, for every row in role_permissions.</summary>
    Public Function GetRoleRules() As Dictionary(Of String, Boolean)
        Dim d As New Dictionary(Of String, Boolean)(StringComparer.Ordinal)
        For Each row As DataRow In Db.Table("SELECT role, module_key, is_allowed FROM role_permissions").Rows
            d(Permissions.NormalizeRole(row("role").ToString()) & "|" & row("module_key").ToString()) = Convert.ToInt32(row("is_allowed")) <> 0
        Next
        Return d
    End Function

    Private Sub UpsertRole(role As String, moduleKey As String, allowed As Boolean)
        Db.NonQuery("INSERT INTO role_permissions (role, module_key, is_allowed) VALUES (@r, @k, @a) " &
                    "ON DUPLICATE KEY UPDATE is_allowed = @a",
                    Db.Params("@r", role, "@k", moduleKey, "@a", If(allowed, 1, 0)))
    End Sub

    ''' <summary>
    ''' Switches a module for a role. For the Mobile App it also switches the modules the app uses (MobileLinked),
    ''' and when the Mobile App is taken away the phones of those people are signed out.
    ''' </summary>
    Public Function SetRole(role As String, moduleKey As String, allowed As Boolean) As Boolean
        Dim r As String = Permissions.NormalizeRole(role)
        If Array.IndexOf(Permissions.EditableRoles, r) < 0 Then Return False
        If Permissions.IsSuperadminOnly(moduleKey) Then Return False

        UpsertRole(r, moduleKey, allowed)
        If moduleKey = Permissions.MobileAppKey Then
            For Each linked As String In Permissions.MobileLinked
                UpsertRole(r, linked, allowed)
            Next
            If Not allowed Then EndMobileSessionsForRole(r)
        End If
        Return True
    End Function

    ''' <summary>Puts Admin and Cashier back to the factory settings (per-user settings are kept).</summary>
    Public Sub RestoreRoleDefaults()
        For Each r As String In Permissions.EditableRoles
            For Each m As ModuleInfo In ModuleCatalog.All
                If Permissions.IsSuperadminOnly(m.Key) Then Continue For
                UpsertRole(r, m.Key, Permissions.DefaultAccess(r, m.Key))
            Next
            UpsertRole(r, Permissions.MobileAppKey, Permissions.DefaultAccess(r, Permissions.MobileAppKey))
        Next
    End Sub

    ''' <summary>Admins and Cashiers (the Superadmin always has everything).</summary>
    Public Function GetUsers() As DataTable
        Return Db.Table(
            "SELECT u.id, u.full_name, u.username, u.role, u.is_active, " &
            "(SELECT COUNT(*) FROM user_permissions p WHERE p.user_id = u.id) AS custom_count " &
            "FROM users u WHERE u.role <> 'superadmin' " &
            "ORDER BY FIELD(u.role, 'admin', 'cashier'), u.full_name")
    End Function

    ''' <summary>module_key -> allowed, for the modules where this user does NOT follow the role.</summary>
    Public Function GetUserRules(userId As Integer) As Dictionary(Of String, Boolean)
        Dim d As New Dictionary(Of String, Boolean)(StringComparer.Ordinal)
        For Each row As DataRow In Db.Table("SELECT module_key, is_allowed FROM user_permissions WHERE user_id = @id",
                                            Db.Params("@id", userId)).Rows
            d(row("module_key").ToString()) = Convert.ToInt32(row("is_allowed")) <> 0
        Next
        Return d
    End Function

    ''' <summary>allowed = Nothing: follow the role again. Never for the Superadmin or Superadmin-only modules.</summary>
    Public Function SetUser(userId As Integer, moduleKey As String, allowed As Boolean?) As Boolean
        If Permissions.IsSuperadminOnly(moduleKey) Then Return False
        Dim role As String = Permissions.NormalizeRole(UserRepository.GetRole(userId))
        If Array.IndexOf(Permissions.EditableRoles, role) < 0 Then Return False

        ' the Mobile App moves together with the modules the app uses
        Dim keys As New List(Of String)()
        keys.Add(moduleKey)
        If moduleKey = Permissions.MobileAppKey Then keys.AddRange(Permissions.MobileLinked)

        For Each k As String In keys
            If allowed.HasValue Then
                Db.NonQuery("INSERT INTO user_permissions (user_id, module_key, is_allowed) VALUES (@u, @k, @a) " &
                            "ON DUPLICATE KEY UPDATE is_allowed = @a",
                            Db.Params("@u", userId, "@k", k, "@a", If(allowed.Value, 1, 0)))
            Else
                Db.NonQuery("DELETE FROM user_permissions WHERE user_id = @u AND module_key = @k",
                            Db.Params("@u", userId, "@k", k))
            End If
        Next
        NotifyChanged()

        ' denied, or back to following a role that does not give the app: their phone is signed out
        If moduleKey = Permissions.MobileAppKey Then
            Dim stillAllowed As Boolean = If(allowed.HasValue, allowed.Value, RoleGivesMobileApp(role))
            If Not stillAllowed Then EndMobileSessionsForUser(userId)
        End If
        Return True
    End Function

    ''' <summary>The user follows their role for every module again.</summary>
    Public Sub ClearUser(userId As Integer)
        Db.NonQuery("DELETE FROM user_permissions WHERE user_id = @u", Db.Params("@u", userId))
        NotifyChanged()
        Dim role As String = Permissions.NormalizeRole(UserRepository.GetRole(userId))
        If Not RoleGivesMobileApp(role) Then EndMobileSessionsForUser(userId)
    End Sub

    ''' <summary>What the role gives for the Mobile App, read from the database (the factory setting if there is no row yet).</summary>
    Private Function RoleGivesMobileApp(role As String) As Boolean
        Try
            Dim o As Object = Db.Scalar("SELECT is_allowed FROM role_permissions WHERE role = @r AND module_key = @k",
                                        Db.Params("@r", role, "@k", Permissions.MobileAppKey))
            If o IsNot Nothing AndAlso o IsNot DBNull.Value Then Return Convert.ToInt32(o) <> 0
        Catch
            ' fall through to the factory setting
        End Try
        Return Permissions.DefaultAccess(role, Permissions.MobileAppKey)
    End Function

    ''' <summary>
    ''' Signs out the phones of everyone with this role who does not have their own Allow for the Mobile App.
    ''' The phone finds out on its next request (a few seconds) and goes back to the login screen.
    ''' </summary>
    Private Sub EndMobileSessionsForRole(role As String)
        Try
            Db.NonQuery("UPDATE user_sessions s INNER JOIN users u ON u.id = s.user_id " &
                        "SET s.is_active = 0, s.logout_at = NOW(), s.end_reason = 'Mobile app access removed' " &
                        "WHERE s.session_id LIKE 'mobile-%' AND s.is_active = 1 AND u.role = @r " &
                        "AND NOT EXISTS (SELECT 1 FROM user_permissions p WHERE p.user_id = u.id " &
                        "AND p.module_key = @k AND p.is_allowed = 1)",
                        Db.Params("@r", role, "@k", Permissions.MobileAppKey))
        Catch
            ' ignore: the phone's next request still checks the permission
        End Try
    End Sub

    Private Sub EndMobileSessionsForUser(userId As Integer)
        Try
            Db.NonQuery("UPDATE user_sessions SET is_active = 0, logout_at = NOW(), end_reason = 'Mobile app access removed' " &
                        "WHERE session_id LIKE 'mobile-%' AND is_active = 1 AND user_id = @u",
                        Db.Params("@u", userId))
        Catch
            ' ignore
        End Try
    End Sub

    ' user_permissions has no trigger (the app's database account cannot create triggers), so tell the other laptops here
    Private Sub NotifyChanged()
        Try
            Db.NonQuery("INSERT INTO change_log (module) VALUES ('user_permissions')")
        Catch
            ' ignore: the others still pick it up at their next login
        End Try
    End Sub

End Module