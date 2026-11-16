Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Data
Imports System.Drawing
Imports System.Globalization

''' <summary>Everything the home dashboard shows, read in one go on a background thread.</summary>
Public Class DashboardModel
    ' which parts this person may see (from their permissions)
    Public ShowSales As Boolean
    Public OwnSalesOnly As Boolean
    Public ShowStock As Boolean
    Public ShowPeople As Boolean
    Public ShowDeliveries As Boolean
    ''' <summary>Costs, profit and stock value (never for a Cashier).</summary>
    Public ShowCosts As Boolean
    ''' <summary>Discounts, special prices and points given (Loss Report).</summary>
    Public ShowGiven As Boolean
    Public ShowCustomers As Boolean

    Public SalesToday As Decimal
    Public SalesYesterday As Decimal
    Public CountToday As Integer
    Public CountYesterday As Integer
    Public ItemsToday As Decimal
    Public SalesMonth As Decimal
    Public SalesLastMonthSoFar As Decimal
    Public Week As ReportChart
    Public TopProducts As ReportChart
    Public RecentSales As DataTable

    Public LowCount As Integer
    Public OutCount As Integer
    Public StockValue As Decimal
    Public ProductCount As Integer
    Public LowStock As DataTable

    Public OnlineCount As Integer
    Public Online As DataTable

    Public Deliveries As DataTable
    Public DeliveriesMonth As Integer
    Public DeliveryCostMonth As Decimal
    Public DraftDeliveries As Integer

    Public ProfitMonth As Decimal
    Public GivenMonth As Decimal
    ''' <summary>Recent discounts, special prices and points, newest first.</summary>
    Public Losses As DataTable

    Public CustomerCount As Integer
    Public PointsOwed As Decimal
    Public PointsPesoValue As Decimal

    Public LoadedAt As DateTime = DateTime.Now
End Class

''' <summary>
''' Queries for the home dashboard. Each part is only read when the person may open the matching module:
''' sales (Sales Report or POS), stock (Inventory, Products or Stock Threshold), people (User Monitoring or Users),
''' deliveries (Delivery Management or Delivery Report). A Cashier sees only their own sales, like the Sales Report.
''' </summary>
Public Module DashboardData

    Private ReadOnly Inv As CultureInfo = CultureInfo.InvariantCulture
    Private ReadOnly PaleBar As Color = Color.FromArgb(205, 165, 165)

    Private Function Dec(o As Object) As Decimal
        If o Is Nothing OrElse o Is DBNull.Value Then Return 0D
        Return Convert.ToDecimal(o)
    End Function

    Private Function Can(role As String, ParamArray keys As String()) As Boolean
        For Each k As String In keys
            If Permissions.CanAccess(role, k) Then Return True
        Next
        Return False
    End Function

    Public Function Load(role As String, userId As Integer) As DashboardModel
        Dim m As New DashboardModel()
        Dim r As String = Permissions.NormalizeRole(role)
        m.ShowSales = Can(role, "sales_report", "pos")
        m.OwnSalesOnly = (r = "cashier")
        m.ShowStock = Can(role, "inventory", "products", "stock_threshold")
        m.ShowPeople = Can(role, "user_monitoring", "users")
        m.ShowDeliveries = Can(role, "delivery", "delivery_report")
        m.ShowCosts = r <> "cashier" AndAlso Can(role, "inventory", "inventory_report", "delivery", "delivery_report", "loss_report")
        m.ShowGiven = Can(role, "loss_report")
        m.ShowCustomers = Can(role, "customers")

        If m.ShowSales Then LoadSales(m, userId)
        If m.ShowStock Then LoadStock(m)
        If m.ShowPeople Then LoadPeople(m, r)
        If m.ShowDeliveries Then LoadDeliveries(m)
        If m.ShowGiven Then LoadGiven(m)
        If m.ShowCustomers Then LoadCustomers(m)
        m.LoadedAt = DateTime.Now
        Return m
    End Function

    ''' <summary>"OR-2026-000012" -> "#000012" (anything else is left as it is).</summary>
    Public Function ShortReceipt(receipt As String) As String
        Dim mt As System.Text.RegularExpressions.Match =
            System.Text.RegularExpressions.Regex.Match(If(receipt, ""), "^[A-Za-z]+-\d{4}-(\d+)$")
        Return If(mt.Success, "#" & mt.Groups(1).Value, receipt)
    End Function

    Private Function MonthStart() As DateTime
        Return New DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)
    End Function

    ' ------------------------------------------------------------------ given away: discounts, special prices, points
    Private Sub LoadGiven(m As DashboardModel)
        Dim prm As Dictionary(Of String, Object) = Db.Params("@month", MonthStart())
        m.GivenMonth = Dec(Db.Scalar(
            "SELECT COALESCE(SUM(s.discount_amount + s.special_discount_amount + s.points_value), 0) " &
            "FROM sales s WHERE s.created_at >= @month", prm))

        ' the latest receipts where something was given up
        m.Losses = Db.Table(
            "SELECT s.created_at AS at, " &
            "CASE WHEN s.special_discount_amount > 0 THEN 'Special price' " &
            "WHEN s.discount_amount > 0 THEN COALESCE(s.discount_name, 'Discount') ELSE 'Points used' END AS kind, " &
            "s.receipt_no AS detail, (s.discount_amount + s.special_discount_amount + s.points_value) AS amount " &
            "FROM sales s WHERE (s.discount_amount + s.special_discount_amount + s.points_value) > 0 " &
            "ORDER BY s.created_at DESC LIMIT 6")
        m.Losses.Columns.Add("when_text", GetType(String))
        For Each row As DataRow In m.Losses.Rows
            Dim at As DateTime = Convert.ToDateTime(row("at"))
            row("when_text") = If(at >= DateTime.Today, at.ToString("h:mm tt", Inv), at.ToString("MMM d", Inv))
            row("detail") = ShortReceipt(row("detail").ToString())
        Next
    End Sub

    ' ------------------------------------------------------------------ loyalty customers
    Private Sub LoadCustomers(m As DashboardModel)
        Dim t As DataTable = Db.Table("SELECT COUNT(*) AS n, COALESCE(SUM(points_balance), 0) AS pts FROM customers")
        m.CustomerCount = CInt(Dec(t.Rows(0)("n")))
        m.PointsOwed = Dec(t.Rows(0)("pts"))
        Dim pv As Decimal = 1D
        Decimal.TryParse(PosRepository.ShopSetting("point_peso_value", "1"), NumberStyles.AllowDecimalPoint, Inv, pv)
        m.PointsPesoValue = pv
    End Sub

    ' ------------------------------------------------------------------ sales
    Private Sub LoadSales(m As DashboardModel, userId As Integer)
        Dim today As DateTime = DateTime.Today
        Dim yday As DateTime = today.AddDays(-1)
        Dim month As New DateTime(today.Year, today.Month, 1)
        Dim lastMonth As DateTime = month.AddMonths(-1)
        Dim lastMonthSameDay As DateTime = lastMonth.AddDays(Math.Min(today.Day, DateTime.DaysInMonth(lastMonth.Year, lastMonth.Month)))
        Dim week As DateTime = today.AddDays(-6)
        Dim own As String = If(m.OwnSalesOnly, " AND s.cashier_id = @u", "")
        Dim prm As Dictionary(Of String, Object) = Db.Params("@today", today, "@yday", yday, "@month", month,
                                                              "@lm", lastMonth, "@lmd", lastMonthSameDay, "@week", week, "@u", userId)

        Dim t As DataTable = Db.Table(
            "SELECT " &
            "COALESCE(SUM(CASE WHEN s.created_at >= @today THEN s.total END), 0) AS st, " &
            "COALESCE(SUM(s.created_at >= @today), 0) AS ct, " &
            "COALESCE(SUM(CASE WHEN s.created_at >= @yday AND s.created_at < @today THEN s.total END), 0) AS sy, " &
            "COALESCE(SUM(s.created_at >= @yday AND s.created_at < @today), 0) AS cy, " &
            "COALESCE(SUM(CASE WHEN s.created_at >= @month THEN s.total END), 0) AS sm, " &
            "COALESCE(SUM(CASE WHEN s.created_at >= @lm AND s.created_at < @lmd THEN s.total END), 0) AS slm " &
            "FROM sales s WHERE s.created_at >= @lm" & own, prm)
        Dim row As DataRow = t.Rows(0)
        m.SalesToday = Dec(row("st"))
        m.CountToday = CInt(Dec(row("ct")))
        m.SalesYesterday = Dec(row("sy"))
        m.CountYesterday = CInt(Dec(row("cy")))
        m.SalesMonth = Dec(row("sm"))
        m.SalesLastMonthSoFar = Dec(row("slm"))

        m.ItemsToday = Dec(Db.Scalar("SELECT COALESCE(SUM(si.qty), 0) FROM sale_items si JOIN sales s ON s.id = si.sale_id " &
                                     "WHERE s.created_at >= @today" & own, prm))

        ' the last 7 days, today included
        Dim perDay As New Dictionary(Of String, Decimal)()
        For Each d As DataRow In Db.Table("SELECT DATE_FORMAT(s.created_at, '%Y-%m-%d') AS k, SUM(s.total) AS v FROM sales s " &
                                          "WHERE s.created_at >= @week" & own & " GROUP BY 1", prm).Rows
            perDay(d("k").ToString()) = Dec(d("v"))
        Next
        m.Week = New ReportChart()
        m.Week.Title = If(m.OwnSalesOnly, "My sales, last 7 days", "Sales, last 7 days")
        m.Week.IsMoney = True
        ' gross profit per day = what was paid minus what the pieces cost (only for people who see costs)
        Dim costPerDay As New Dictionary(Of String, Decimal)()
        If m.ShowCosts AndAlso Not m.OwnSalesOnly Then
            For Each d As DataRow In Db.Table("SELECT DATE_FORMAT(s.created_at, '%Y-%m-%d') AS k, SUM(si.qty * si.cost_price) AS c " &
                                              "FROM sale_items si JOIN sales s ON s.id = si.sale_id " &
                                              "WHERE s.created_at >= @week GROUP BY 1", prm).Rows
                costPerDay(d("k").ToString()) = Dec(d("c"))
            Next
            m.ProfitMonth = m.SalesMonth - Dec(Db.Scalar("SELECT COALESCE(SUM(si.qty * si.cost_price), 0) FROM sale_items si " &
                                                         "JOIN sales s ON s.id = si.sale_id WHERE s.created_at >= @month", prm))
        End If

        Dim series As New ChartSeries("Sales", Theme.Maroon)
        Dim profit As New ChartSeries("Gross profit", PaleBar)
        For i As Integer = 0 To 6
            Dim day As DateTime = week.AddDays(i)
            Dim key As String = day.ToString("yyyy-MM-dd", Inv)
            m.Week.Labels.Add(If(i = 6, "Today", day.ToString("ddd d", Inv)))
            Dim v As Decimal = 0D
            perDay.TryGetValue(key, v)
            series.Values.Add(v)
            Dim c As Decimal = 0D
            costPerDay.TryGetValue(key, c)
            profit.Values.Add(If(v > 0D, v - c, 0D))
        Next
        m.Week.Series.Add(series)
        If m.ShowCosts AndAlso Not m.OwnSalesOnly Then
            m.Week.Title = "Sales and gross profit, last 7 days"
            m.Week.Series.Add(profit)
        End If

        m.TopProducts = New ReportChart()
        m.TopProducts.Title = "Top products, last 7 days (pieces)"
        m.TopProducts.Horizontal = True
        Dim top As New ChartSeries("Pieces", Theme.Maroon)
        For Each p As DataRow In Db.Table(
                "SELECT si.product_name AS n, si.measurement AS sz, SUM(si.qty) AS q " &
                "FROM sale_items si JOIN sales s ON s.id = si.sale_id WHERE s.created_at >= @week" & own &
                " GROUP BY si.product_name, si.measurement ORDER BY q DESC LIMIT 5", prm).Rows
            Dim sz As String = p("sz").ToString()
            m.TopProducts.Labels.Add(p("n").ToString() & If(sz.Length > 0 AndAlso sz <> "-", " " & sz, ""))
            top.Values.Add(Dec(p("q")))
        Next
        m.TopProducts.Series.Add(top)

        m.RecentSales = Db.Table(
            "SELECT s.receipt_no, s.created_at, COALESCE(s.cashier_name, '') AS cashier, s.total " &
            "FROM sales s WHERE 1 = 1" & own & " ORDER BY s.id DESC LIMIT 6", prm)
        ' today's sales show the time, older ones the date; the receipt as its number only (the lists are narrow)
        m.RecentSales.Columns.Add("when_text", GetType(String))
        m.RecentSales.Columns.Add("receipt_short", GetType(String))
        For Each r As DataRow In m.RecentSales.Rows
            Dim at As DateTime = Convert.ToDateTime(r("created_at"))
            r("when_text") = If(at >= today, at.ToString("h:mm tt", Inv), at.ToString("MMM d", Inv))
            r("receipt_short") = ShortReceipt(r("receipt_no").ToString())
        Next
    End Sub

    ' ------------------------------------------------------------------ stock
    Private Sub LoadStock(m As DashboardModel)
        Dim t As DataTable = Db.Table(
            "SELECT COUNT(*) AS n, " &
            "COALESCE(SUM(p.low_stock_threshold > 0 AND p.qty_on_hand > 0 AND p.qty_on_hand <= p.low_stock_threshold), 0) AS low, " &
            "COALESCE(SUM(p.qty_on_hand <= 0), 0) AS outs, " &
            "COALESCE(SUM(GREATEST(p.qty_on_hand, 0) * p.cost_price), 0) AS value " &
            "FROM products p")
        Dim row As DataRow = t.Rows(0)
        m.ProductCount = CInt(Dec(row("n")))
        m.LowCount = CInt(Dec(row("low")))
        m.OutCount = CInt(Dec(row("outs")))
        m.StockValue = Dec(row("value"))

        ' the same rule as the red low-stock line: at or under the minimum, empty ones first
        m.LowStock = Db.Table(
            "SELECT TRIM(CONCAT(p.name, ' ', p.measurement)) AS product, p.qty_on_hand AS qty, p.low_stock_threshold AS min_qty " &
            "FROM products p WHERE p.qty_on_hand <= 0 OR (p.low_stock_threshold > 0 AND p.qty_on_hand <= p.low_stock_threshold) " &
            "ORDER BY (p.qty_on_hand <= 0) DESC, p.qty_on_hand / GREATEST(p.low_stock_threshold, 1), p.name LIMIT 6")
    End Sub

    ' ------------------------------------------------------------------ people
    Private Sub LoadPeople(m As DashboardModel, viewerRole As String)
        Dim hideSuper As String = If(viewerRole = "superadmin", "", " AND u.role <> 'superadmin'")
        m.Online = Db.Table(
            "SELECT u.full_name AS person, u.role, COALESCE(s.current_module, '') AS screen, COALESCE(s.device_name, '') AS device " &
            "FROM user_sessions s JOIN users u ON u.id = s.user_id " &
            "WHERE s.is_active = 1 AND s.last_seen >= DATE_SUB(NOW(), INTERVAL 30 SECOND)" & hideSuper &
            " ORDER BY s.login_at LIMIT 6")
        m.OnlineCount = CInt(Dec(Db.Scalar(
            "SELECT COUNT(DISTINCT s.user_id) FROM user_sessions s JOIN users u ON u.id = s.user_id " &
            "WHERE s.is_active = 1 AND s.last_seen >= DATE_SUB(NOW(), INTERVAL 30 SECOND)" & hideSuper)))
        m.Online.Columns.Add("role_text", GetType(String))
        For Each r As DataRow In m.Online.Rows
            r("role_text") = Permissions.RoleDisplay(r("role").ToString())
        Next
    End Sub

    ' ------------------------------------------------------------------ deliveries
    Private Sub LoadDeliveries(m As DashboardModel)
        m.Deliveries = Db.Table(
            "SELECT d.batch_no, sp.company_name AS supplier, d.delivery_date, d.total_cost, " &
            "IF(d.status = 'sent', 'Received', 'Draft') AS status " &
            "FROM deliveries d JOIN suppliers sp ON sp.id = d.supplier_id ORDER BY d.id DESC LIMIT 6")
        Dim t As DataTable = Db.Table(
            "SELECT COALESCE(SUM(d.status = 'sent' AND d.delivery_date >= @month), 0) AS n, " &
            "COALESCE(SUM(CASE WHEN d.status = 'sent' AND d.delivery_date >= @month THEN d.total_cost END), 0) AS cost, " &
            "COALESCE(SUM(d.status = 'draft'), 0) AS drafts FROM deliveries d", Db.Params("@month", MonthStart()))
        m.DeliveriesMonth = CInt(Dec(t.Rows(0)("n")))
        m.DeliveryCostMonth = Dec(t.Rows(0)("cost"))
        m.DraftDeliveries = CInt(Dec(t.Rows(0)("drafts")))
    End Sub

End Module
