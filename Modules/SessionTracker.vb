Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Net
Imports System.Net.Sockets
Imports System.Threading

''' <summary>
''' Records who is logged in, on which PC, since when, and which screen they are in (user_sessions).
''' A person counts as "active" while their heartbeat is fresh (last 30 seconds).
''' </summary>
Public Module SessionTracker

    Private _sessionId As String = ""

    ''' <summary>This login's row in user_sessions ("" when none). A system reset keeps it.</summary>
    Public ReadOnly Property CurrentSessionId As String
        Get
            Return _sessionId
        End Get
    End Property

    Public Sub StartSession()
        _sessionId = Guid.NewGuid().ToString("N")
        Try
            Db.NonQuery(
                "INSERT INTO user_sessions (session_id, user_id, username, device_name, ip_address, current_module, module_since) " &
                "VALUES (@s, @u, @n, @d, @ip, 'Home', NOW())",
                Db.Params("@s", _sessionId, "@u", AppSession.UserId, "@n", AppSession.Username,
                          "@d", Environment.MachineName, "@ip", LocalIp()))
        Catch
            _sessionId = ""
            Return
        End Try
        Try
            EndOtherSessions(_sessionId)
        Catch
            ' the other computer still finds out at its next 15-second check
        End Try
    End Sub

    ' ------------------------------------------------------------------ one computer per account
    ''' <summary>Written in end_reason when a session was closed because the same account logged in elsewhere.</summary>
    Public Const OtherLoginReason As String = "other_login"

    ''' <summary>
    ''' Computers where this account is logged in right now (fresh heartbeat, not ended).
    ''' Asked before a log-in finishes: one account works on one computer at a time.
    ''' </summary>
    Public Function LiveDevicesOf(userId As Integer) As List(Of String)
        Dim result As New List(Of String)()
        Dim dt As System.Data.DataTable = Db.Table(
            "SELECT DISTINCT COALESCE(device_name, '') AS d FROM user_sessions " &
            "WHERE user_id = @u AND is_active = 1 AND end_reason IS NULL " &
            "AND last_seen >= DATE_SUB(NOW(), INTERVAL 30 SECOND)",
            Db.Params("@u", userId))
        For Each r As System.Data.DataRow In dt.Rows
            result.Add(r("d").ToString())
        Next
        Return result
    End Function

    ''' <summary>
    ''' After this computer's session started: every other open session of the same account is ended
    ''' (those computers notice within about a second and go back to the login screen).
    ''' A session that was already silent keeps its last report as its log-out time, so duty records stay right.
    ''' </summary>
    Private Sub EndOtherSessions(sid As String)
        Dim n As Integer = Db.NonQuery(
            "UPDATE user_sessions SET is_active = 0, end_reason = @r, " &
            "logout_at = COALESCE(logout_at, IF(last_seen >= DATE_SUB(NOW(), INTERVAL 30 SECOND), NOW(), last_seen)) " &
            "WHERE user_id = @u AND session_id <> @s AND end_reason IS NULL AND (is_active = 1 OR logout_at IS NULL)",
            Db.Params("@r", OtherLoginReason & "|" & Left(Environment.MachineName, 80), "@u", AppSession.UserId, "@s", sid))
        If n > 0 Then
            Db.NonQuery("INSERT INTO change_log (module) VALUES ('user_sessions')")
        End If
    End Sub

    ''' <summary>
    ''' Was this computer's session ended because the account logged in somewhere else? Returns that computer's name,
    ''' or Nothing. Checked by the live engine.
    ''' </summary>
    Public Function EndedByOtherLogin() As String
        Dim sid As String = _sessionId
        If sid.Length = 0 Then Return Nothing
        Dim o As Object = Db.Scalar("SELECT end_reason FROM user_sessions WHERE session_id = @s", Db.Params("@s", sid))
        If o Is Nothing OrElse o Is DBNull.Value Then Return Nothing
        Dim reason As String = o.ToString()
        If Not reason.StartsWith(OtherLoginReason, StringComparison.Ordinal) Then Return Nothing
        Dim bar As Integer = reason.IndexOf("|"c)
        Return If(bar >= 0, reason.Substring(bar + 1), "another computer")
    End Function

    ''' <summary>Forget this computer's session without writing anything (it was ended from another computer).</summary>
    Public Sub Abandon()
        _sessionId = ""
    End Sub

    Private Function Left(s As String, n As Integer) As String
        If s Is Nothing OrElse s.Length <= n Then Return s
        Return s.Substring(0, n)
    End Function

    ''' <summary>Called every few seconds from the live engine (background thread).</summary>
    Public Sub Heartbeat()
        Dim sid As String = _sessionId
        If sid.Length = 0 Then Return
        Try
            ' a session ended by a log-in elsewhere stays ended
            Db.NonQuery("UPDATE user_sessions SET last_seen = NOW(), is_active = 1 WHERE session_id = @s AND end_reason IS NULL",
                        Db.Params("@s", sid))
        Catch
            ' ignore
        End Try
    End Sub

    ''' <summary>"This person just opened the Sales Report" (does not slow the screen down).</summary>
    Public Sub SetModuleAsync(moduleName As String)
        Dim sid As String = _sessionId
        If sid.Length = 0 Then Return

        ' capture who this is now: the work below runs on another thread
        Dim uid As Integer = AppSession.UserId
        Dim uname As String = AppSession.Username
        Dim logIt As Boolean = (moduleName <> "Home")

        ThreadPool.QueueUserWorkItem(
            Sub(state As Object)
                Try
                    Db.NonQuery(
                        "UPDATE user_sessions SET current_module = @m, module_since = NOW(), last_seen = NOW() WHERE session_id = @s",
                        Db.Params("@m", moduleName, "@s", sid))
                    If logIt Then
                        Db.NonQuery(
                            "INSERT INTO audit_trail (user_id, username, action, module, description, device_name) " &
                            "VALUES (@uid, @un, 'OPEN', @m, @d, @dev)",
                            Db.Params("@uid", uid, "@un", uname, "@m", moduleName,
                                      "@d", "Opened " & moduleName, "@dev", Environment.MachineName))
                    End If
                Catch
                    ' ignore
                End Try
            End Sub)
    End Sub

    Public Sub EndSession()
        Dim sid As String = _sessionId
        _sessionId = ""
        If sid.Length = 0 Then Return
        Try
            Db.NonQuery("UPDATE user_sessions SET is_active = 0, logout_at = NOW() WHERE session_id = @s AND end_reason IS NULL",
                        Db.Params("@s", sid))
            AuditLog.Write("LOGOUT", "Login", "'" & AppSession.Username & "' logged out")
        Catch
            ' ignore
        End Try
    End Sub

    ''' <summary>This PC's IPv4 address, read from the network adapters (no DNS lookup, which can stall for seconds).</summary>
    Private Function LocalIp() As String
        Try
            Dim fallback As String = Nothing
            For Each ni As System.Net.NetworkInformation.NetworkInterface In System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                If ni.OperationalStatus <> System.Net.NetworkInformation.OperationalStatus.Up Then Continue For
                If ni.NetworkInterfaceType = System.Net.NetworkInformation.NetworkInterfaceType.Loopback OrElse
                   ni.NetworkInterfaceType = System.Net.NetworkInformation.NetworkInterfaceType.Tunnel Then Continue For

                Dim props As System.Net.NetworkInformation.IPInterfaceProperties = ni.GetIPProperties()
                For Each ua As System.Net.NetworkInformation.UnicastIPAddressInformation In props.UnicastAddresses
                    If ua.Address.AddressFamily <> AddressFamily.InterNetwork Then Continue For
                    If props.GatewayAddresses.Count > 0 Then Return ua.Address.ToString()   ' the real LAN / Wi-Fi adapter
                    If fallback Is Nothing Then fallback = ua.Address.ToString()
                Next
            Next
            Return fallback
        Catch
            ' ignore
        End Try
        Return Nothing
    End Function

End Module
