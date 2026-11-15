Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Data
Imports System.Drawing
Imports System.Globalization

''' <summary>
''' Queries for Utilities > Audit Trail: a read-only record of everything that happens in the system
''' (the rows written by AuditLog.Write). Returns ReportDocs, so the screen, the PDF, Excel and the printout agree.
''' </summary>
Public Module AuditData

    Private ReadOnly Inv As CultureInfo = CultureInfo.InvariantCulture
    Private Const MaxRows As Integer = 3000
    Private Const InRange As String = "a.created_at >= @f AND a.created_at < @t"

    ' action code -> readable text
    Private ReadOnly Labels As New Dictionary(Of String, String)() From {
        {"LOGIN", "Logged in"}, {"LOGOUT", "Logged out"}, {"LOGIN_FAILED", "Failed login"}, {"UNLOCK", "Unlocked account"},
        {"RESET_PASSWORD", "Reset password"}, {"ENABLE", "Enabled user"}, {"DISABLE", "Disabled user"}, {"CREATE", "Created"},
        {"SALE", "Sale"}, {"LOYALTY", "Loyalty points"}, {"REPRINT", "Receipt reprint"}, {"CARD_SAVED", "Card saved"}, {"CARD_PRINTED", "Card printed"},
        {"SPECIAL_PRICE", "Special price"}, {"OVERRIDE_FAILED", "Failed override"}, {"PRICE_CHANGE", "Price change"},
        {"ADD", "Added"}, {"EDIT", "Edited"}, {"DELETE", "Deleted"}, {"ACTIVATE", "Activated"}, {"DEACTIVATE", "Deactivated"}, {"SEND", "Delivery sent"},
        {"EXPORT", "Exported report"}, {"PRINT", "Printed report"}, {"PRINT_BARCODE", "Printed barcode"}, {"DEVICE_TEST", "Device test"},
        {"OPEN", "Opened a screen"}, {"PERMISSION", "Permission changed"}, {"VERIFY_FAILED", "Wrong password check"},
        {"BACKUP", "Backup made"}, {"RESTORE", "Backup restored"}, {"RESET", "System reset"}}

    ' action code -> group, so the list can be searched by "Access", "Sales", "Prices", "Changes", "Reports"
    Private ReadOnly Groups As New Dictionary(Of String, String)() From {
        {"LOGIN", "Access"}, {"LOGOUT", "Access"}, {"LOGIN_FAILED", "Access"}, {"UNLOCK", "Access"}, {"RESET_PASSWORD", "Access"},
        {"ENABLE", "Access"}, {"DISABLE", "Access"}, {"CREATE", "Access"},
        {"SALE", "Sales"}, {"LOYALTY", "Sales"}, {"REPRINT", "Sales"}, {"CARD_SAVED", "Sales"}, {"CARD_PRINTED", "Sales"},
        {"SPECIAL_PRICE", "Prices"}, {"OVERRIDE_FAILED", "Prices"}, {"PRICE_CHANGE", "Prices"},
        {"ADD", "Changes"}, {"EDIT", "Changes"}, {"DELETE", "Changes"}, {"ACTIVATE", "Changes"}, {"DEACTIVATE", "Changes"}, {"SEND", "Changes"},
        {"EXPORT", "Reports"}, {"PRINT", "Reports"}, {"PRINT_BARCODE", "Reports"}, {"DEVICE_TEST", "System"},
        {"PERMISSION", "Access"}, {"VERIFY_FAILED", "Access"},
        {"BACKUP", "System"}, {"RESTORE", "System"}, {"RESET", "System"}}

    Private Function Txt(o As Object) As String
        If o Is Nothing OrElse o Is DBNull.Value Then Return ""
        Return Convert.ToString(o, Inv)
    End Function

    Private Function Dec(o As Object) As Decimal
        If o Is Nothing OrElse o Is DBNull.Value Then Return 0D
        Return Convert.ToDecimal(o)
    End Function

    Private Function Pct(part As Decimal, whole As Decimal) As Decimal
        If whole = 0D Then Return 0D
        Return Math.Round(part / whole * 100D, 1)
    End Function

    Public Function ActionLabel(action As String) As String
        Dim s As String = Nothing
        If action IsNot Nothing AndAlso Labels.TryGetValue(action, s) Then Return s
        If String.IsNullOrEmpty(action) Then Return ""
        Dim t As String = action.Replace("_"c, " "c).ToLowerInvariant()
        Return Char.ToUpperInvariant(t(0)) & t.Substring(1)
    End Function

    Public Function ActionGroup(action As String) As String
        Dim s As String = Nothing
        If action IsNot Nothing AndAlso Groups.TryGetValue(action, s) Then Return s
        Return "Other"
    End Function

    Private Function RoleText(role As String) As String
        Dim r As String = Permissions.NormalizeRole(role)
        If r.Length = 0 Then Return ""
        Return Char.ToUpperInvariant(r(0)) & r.Substring(1)
    End Function

    Private Function Prm(r As DateRange) As Dictionary(Of String, Object)
        Return Db.Params("@f", r.From, "@t", r.ToExclusive)
    End Function

    Private Function NewDoc(title As String, r As DateRange) As ReportDoc
        Dim d As New ReportDoc()
        d.ShopName = PosRepository.ShopSetting("shop_name", "DARWIN TRADING")
        d.Title = title
        d.PeriodLabel = r.Label
        d.GeneratedBy = AppSession.FullName
        d.GeneratedAt = DateTime.Now
        d.Note = "A read-only record: entries cannot be edited or deleted."
        Return d
    End Function

    ''' <summary>The newest audit row number (the screen uses it to notice new activity).</summary>
    Public Function LatestId() As Long
        Return Convert.ToInt64(Dec(Db.Scalar("SELECT COALESCE(MAX(id), 0) FROM audit_trail")))
    End Function

    Private Sub AddKpis(doc As ReportDoc, r As DateRange)
        Dim t As DataTable = Db.Table("SELECT COUNT(*) AS n, COUNT(DISTINCT a.user_id) AS people, " &
                                      "COALESCE(SUM(a.action = 'LOGIN'), 0) AS logins, COALESCE(SUM(a.action = 'LOGIN_FAILED'), 0) AS failed, " &
                                      "COALESCE(SUM(a.action = 'DELETE'), 0) AS deletions, " &
                                      "COALESCE(SUM(a.action IN ('SPECIAL_PRICE', 'PRICE_CHANGE', 'OVERRIDE_FAILED')), 0) AS prices " &
                                      "FROM audit_trail a WHERE " & InRange, Prm(r))
        Dim failed As Decimal = Dec(t.Rows(0)("failed"))
        Dim deletions As Decimal = Dec(t.Rows(0)("deletions"))
        doc.Kpis.Add(New ReportKpi("Actions recorded", ReportFormat.Whole(Dec(t.Rows(0)("n")))))
        doc.Kpis.Add(New ReportKpi("People active", ReportFormat.Whole(Dec(t.Rows(0)("people")))))
        doc.Kpis.Add(New ReportKpi("Logins", ReportFormat.Whole(Dec(t.Rows(0)("logins")))))
        doc.Kpis.Add(New ReportKpi("Failed logins", ReportFormat.Whole(failed), failed > 0D))
        doc.Kpis.Add(New ReportKpi("Deletions", ReportFormat.Whole(deletions), deletions > 0D))
        doc.Kpis.Add(New ReportKpi("Price overrides", ReportFormat.Whole(Dec(t.Rows(0)("prices")))))
    End Sub

    ' =====================================================================================
    '  VIEW 1: the activity log (every record, newest first)
    ' =====================================================================================
    Public Function ActivityLog(p As ReportPeriod, r As DateRange) As ReportDoc
        Dim doc As ReportDoc = NewDoc("Audit Trail", r)
        AddKpis(doc, r)

        Dim dt As DataTable = Db.Table(
            "SELECT a.id, a.created_at, COALESCE(NULLIF(u.full_name, ''), NULLIF(a.username, ''), '(system)') AS person, " &
            "COALESCE(u.role, '') AS role, a.action, a.module, COALESCE(a.description, '') AS description, COALESCE(a.device_name, '') AS device " &
            "FROM audit_trail a LEFT JOIN users u ON u.id = a.user_id WHERE " & InRange &
            " ORDER BY a.created_at DESC, a.id DESC LIMIT " & (MaxRows + 1), Prm(r))
        If dt.Rows.Count > MaxRows Then
            dt.Rows.RemoveAt(dt.Rows.Count - 1)
            doc.TruncatedNote = "Showing the latest " & MaxRows.ToString("N0", Inv) & " records of this period. Choose a shorter period or search to narrow it down."
        End If
        dt.Columns.Add("role_text", GetType(String))
        dt.Columns.Add("action_label", GetType(String))
        dt.Columns.Add("category", GetType(String))
        For Each row As DataRow In dt.Rows
            Dim code As String = Txt(row("action"))
            row("role_text") = RoleText(Txt(row("role")))
            row("action_label") = ActionLabel(code)
            row("category") = ActionGroup(code)
        Next

        doc.TableTitle = "Every recorded action"
        doc.Columns.Add(New ReportColumn("DATE & TIME", "created_at", ColKind.Stamp, 15, 170))
        doc.Columns.Add(New ReportColumn("PERSON", "person", ColKind.Text, 13, 120))
        doc.Columns.Add(New ReportColumn("ROLE", "role_text", ColKind.Text, 8, 80))
        doc.Columns.Add(New ReportColumn("ACTION", "action_label", ColKind.Text, 11, 120))
        doc.Columns.Add(New ReportColumn("TYPE", "category", ColKind.Text, 8, 80))
        doc.Columns.Add(New ReportColumn("MODULE", "module", ColKind.Text, 11, 110))
        doc.Columns.Add(New ReportColumn("DETAILS", "description", ColKind.Text, 32, 280))
        doc.Columns.Add(New ReportColumn("PC", "device", ColKind.Text, 9, 90))
        doc.Data = dt
        doc.EmptyText = "Nothing was recorded in this period."
        Return doc
    End Function

    ' =====================================================================================
    '  VIEW 2: summary (what happened, how often)
    ' =====================================================================================
    Public Function Summary(p As ReportPeriod, r As DateRange) As ReportDoc
        Dim doc As ReportDoc = NewDoc("Audit Trail - Summary", r)
        Dim q As Dictionary(Of String, Object) = Prm(r)
        AddKpis(doc, r)

        ' activity per hour / per day
        Dim expr As String = If(p = ReportPeriod.Daily, "HOUR(a.created_at)", "DATE_FORMAT(a.created_at, '%Y-%m-%d')")
        Dim tr As DataTable = Db.Table("SELECT " & expr & " AS k, COUNT(*) AS v FROM audit_trail a WHERE " & InRange & " GROUP BY 1", q)
        Dim counts As New Dictionary(Of String, Decimal)()
        Dim keys As New List(Of String)()
        For Each row As DataRow In tr.Rows
            counts(Txt(row("k"))) = Dec(row("v"))
            keys.Add(Txt(row("k")))
        Next
        Dim labels As New List(Of String)()
        Dim bucketKeys As New List(Of String)()
        If p = ReportPeriod.Daily Then
            Dim lo As Integer = 8
            Dim hi As Integer = 18
            For Each k As String In keys
                Dim hh As Integer
                If Integer.TryParse(k, hh) Then
                    If hh < lo Then lo = hh
                    If hh > hi Then hi = hh
                End If
            Next
            For h As Integer = lo To hi
                bucketKeys.Add(h.ToString(Inv))
                Dim h12 As Integer = h Mod 12
                If h12 = 0 Then h12 = 12
                labels.Add(h12.ToString(Inv) & If(h < 12, "a", "p"))
            Next
        Else
            Dim d As DateTime = r.From
            While d < r.ToExclusive
                bucketKeys.Add(d.ToString("yyyy-MM-dd", Inv))
                labels.Add(If(p = ReportPeriod.Weekly, d.ToString("ddd d", Inv), d.Day.ToString(Inv)))
                d = d.AddDays(1)
            End While
        End If
        Dim trend As New ReportChart()
        trend.Title = If(p = ReportPeriod.Daily, "Actions per hour", "Actions per day")
        Dim ts As New ChartSeries("Actions", Theme.Maroon)
        For i As Integer = 0 To bucketKeys.Count - 1
            trend.Labels.Add(labels(i))
            Dim v As Decimal = 0D
            counts.TryGetValue(bucketKeys(i), v)
            ts.Values.Add(v)
        Next
        trend.Series.Add(ts)
        doc.Charts.Add(trend)

        ' by action and module
        Dim dt As DataTable = Db.Table("SELECT a.action, a.module, COUNT(*) AS n, MAX(a.created_at) AS last_at FROM audit_trail a WHERE " & InRange &
                                       " GROUP BY a.action, a.module ORDER BY n DESC, a.module", q)
        dt.Columns.Add("label", GetType(String))
        dt.Columns.Add("category", GetType(String))
        dt.Columns.Add("share", GetType(Decimal))
        Dim total As Decimal = 0D
        For Each row As DataRow In dt.Rows
            total += Dec(row("n"))
        Next
        Dim byGroup As New Dictionary(Of String, Decimal)()
        For Each row As DataRow In dt.Rows
            Dim code As String = Txt(row("action"))
            row("label") = ActionLabel(code)
            row("category") = ActionGroup(code)
            row("share") = Pct(Dec(row("n")), total)
            Dim g As String = ActionGroup(code)
            Dim cur As Decimal = 0D
            byGroup.TryGetValue(g, cur)
            byGroup(g) = cur + Dec(row("n"))
        Next

        Dim names As New List(Of String)(byGroup.Keys)
        names.Sort(Function(a As String, b As String) byGroup(b).CompareTo(byGroup(a)))
        Dim gc As New ReportChart()
        gc.Title = "Actions by type"
        gc.Horizontal = True
        Dim gs As New ChartSeries("Actions", Theme.Maroon)
        For Each n As String In names
            gc.Labels.Add(n)
            gs.Values.Add(byGroup(n))
        Next
        gc.Series.Add(gs)
        doc.Charts.Add(gc)

        doc.TableTitle = "What happened"
        doc.Columns.Add(New ReportColumn("ACTION", "label", ColKind.Text, 20, 150))
        doc.Columns.Add(New ReportColumn("TYPE", "category", ColKind.Text, 12, 90))
        doc.Columns.Add(New ReportColumn("MODULE", "module", ColKind.Text, 18, 130))
        doc.Columns.Add(New ReportColumn("TIMES", "n", ColKind.Number, 9, 80))
        doc.Columns.Add(New ReportColumn("SHARE", "share", ColKind.Percent, 9, 80))
        doc.Columns.Add(New ReportColumn("LAST TIME", "last_at", ColKind.Stamp, 17, 170))
        doc.Data = dt
        doc.EmptyText = "Nothing was recorded in this period."
        Return doc
    End Function

    ' =====================================================================================
    '  VIEW 3: by person
    ' =====================================================================================
    Public Function ByPerson(p As ReportPeriod, r As DateRange) As ReportDoc
        Dim doc As ReportDoc = NewDoc("Audit Trail - By person", r)
        AddKpis(doc, r)

        Dim dt As DataTable = Db.Table(
            "SELECT COALESCE(a.username, '') AS username, MAX(u.full_name) AS full_name, MAX(u.role) AS role, COUNT(*) AS n, " &
            "COALESCE(SUM(a.action = 'LOGIN'), 0) AS logins, COALESCE(SUM(a.action = 'SALE'), 0) AS sales, " &
            "COALESCE(SUM(a.action IN ('ADD', 'EDIT', 'DELETE', 'ACTIVATE', 'DEACTIVATE', 'SEND', 'ENABLE', 'DISABLE')), 0) AS changes, " &
            "COALESCE(SUM(a.action = 'DELETE'), 0) AS deletions, COALESCE(SUM(a.action = 'LOGIN_FAILED'), 0) AS failed, " &
            "MAX(a.created_at) AS last_at " &
            "FROM audit_trail a LEFT JOIN users u ON u.id = a.user_id WHERE " & InRange &
            " GROUP BY COALESCE(a.username, '') ORDER BY n DESC", Prm(r))
        dt.Columns.Add("person", GetType(String))
        dt.Columns.Add("role_text", GetType(String))
        For Each row As DataRow In dt.Rows
            Dim nm As String = Txt(row("full_name"))
            If nm.Length = 0 Then nm = Txt(row("username"))
            If nm.Length = 0 Then nm = "(system)"
            row("person") = nm
            row("role_text") = RoleText(Txt(row("role")))
        Next

        Dim ch As New ReportChart()
        ch.Title = "Most active people (actions)"
        ch.Horizontal = True
        Dim s As New ChartSeries("Actions", Theme.Maroon)
        Dim shown As Integer = 0
        For Each row As DataRow In dt.Rows
            If shown >= 10 Then Exit For
            ch.Labels.Add(Txt(row("person")))
            s.Values.Add(Dec(row("n")))
            shown += 1
        Next
        ch.Series.Add(s)
        doc.Charts.Add(ch)

        doc.TableTitle = "Actions per person"
        doc.Columns.Add(New ReportColumn("PERSON", "person", ColKind.Text, 18, 150))
        doc.Columns.Add(New ReportColumn("ROLE", "role_text", ColKind.Text, 9, 90))
        doc.Columns.Add(New ReportColumn("ACTIONS", "n", ColKind.Number, 9, 80))
        doc.Columns.Add(New ReportColumn("LOGINS", "logins", ColKind.Number, 8, 80))
        doc.Columns.Add(New ReportColumn("SALES", "sales", ColKind.Number, 8, 80))
        doc.Columns.Add(New ReportColumn("CHANGES", "changes", ColKind.Number, 9, 80))
        doc.Columns.Add(New ReportColumn("DELETIONS", "deletions", ColKind.Number, 9, 90))
        doc.Columns.Add(New ReportColumn("FAILED LOGINS", "failed", ColKind.Number, 10, 100))
        doc.Columns.Add(New ReportColumn("LAST ACTIVITY", "last_at", ColKind.Stamp, 16, 160))
        doc.Data = dt
        doc.EmptyText = "Nothing was recorded in this period."
        Return doc
    End Function

End Module
