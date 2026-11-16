Option Strict On
Option Explicit On

Imports System.Collections.Generic

''' <summary>One explanation behind a "?" on a figure: how it is worked out, and where the numbers come from.</summary>
Public Class FigureExplanation
    Public ReadOnly Property How As String
    Public ReadOnly Property Source As String

    Public Sub New(how As String, source As String)
        Me.How = how
        Me.Source = source
    End Sub
End Class

''' <summary>
''' The explanations behind the "?" on every figure tile (reports, Inventory, User Monitoring, Users, Products ...).
''' Looked up by area (report or screen) and the tile's caption. Edit the wording here: it is the only place.
''' A tile whose caption is not listed simply has no "?".
''' </summary>
Public Module FigureHelp

    ' (declared before Map: module fields are set in order, and Build() uses it)
    Private ReadOnly Peso As String = ChrW(&H20B1)

    Private ReadOnly Map As Dictionary(Of String, FigureExplanation) = Build()

    ''' <summary>The explanation for this tile, or Nothing. "Sold below cost (3)" also finds "Sold below cost".</summary>
    Public Function Find(area As String, caption As String) As FigureExplanation
        If String.IsNullOrEmpty(area) OrElse String.IsNullOrEmpty(caption) Then Return Nothing
        Dim x As FigureExplanation = Nothing
        If Map.TryGetValue(area & "|" & caption, x) Then Return x
        Dim p As Integer = caption.IndexOf(" (", StringComparison.Ordinal)
        If p > 0 AndAlso Map.TryGetValue(area & "|" & caption.Substring(0, p), x) Then Return x
        Return Nothing
    End Function

    Private Const Period As String = "the chosen period (the day, week or month at the top)"
    Private Const CashierNote As String = " A cashier sees only their own sales."

    Private Function Build() As Dictionary(Of String, FigureExplanation)
        Dim d As New Dictionary(Of String, FigureExplanation)(StringComparer.OrdinalIgnoreCase)
        Dim add As Action(Of String, String, String, String) =
            Sub(area As String, caption As String, how As String, source As String)
                d(area & "|" & caption) = New FigureExplanation(how, source)
            End Sub

        ' ============================================================ Home > Dashboard
        Dim db As String = "Dashboard"
        Const ownNote As String = " A cashier sees only their own sales."
        Dim salesToday As String =
            "Adds the total of every receipt made today, since midnight (after discounts, VAT included). The line under it " &
            "compares with the whole of yesterday." & ownNote
        add(db, "Sales today", salesToday, "Every receipt saved by the POS.")
        add(db, "My sales today", salesToday, "Every receipt saved by the POS under your name.")
        add(db, "Transactions",
            "Counts the receipts made today, since midnight. The average under it = Sales today divided by Transactions." & ownNote,
            "Every receipt saved by the POS.")
        add(db, "Items sold today",
            "Adds the quantity of every item on today's receipts, in pieces." & ownNote,
            "The item lines of today's receipts.")
        Dim salesMonth As String =
            "Adds the total of every receipt since the 1st of this month. The line under it compares with last month " &
            "up to the same day (a fair comparison while the month is not over)." & ownNote
        add(db, "Sales this month", salesMonth, "Every receipt saved by the POS.")
        add(db, "My sales this month", salesMonth, "Every receipt saved by the POS under your name.")
        add(db, "Gross profit, month",
            "Sales this month minus what the sold items cost the shop (quantity x the cost price each item had when it " &
            "was sold). The % under it = gross profit divided by sales, times 100. Selling prices include VAT.",
            "Receipt totals, and the cost price saved on each item line (from Delivery Management).")
        add(db, "Given away, month",
            "Adds the discounts, approved special prices and loyalty points used to pay since the 1st of this month. " &
            "The % under it = given away divided by (sales + given away), times 100: the part of the full price customers did not pay.",
            "The DISCOUNT, SPECIAL PRICE and POINTS amounts on every receipt (see the Loss Report for the details).")
        add(db, "Low / out of stock",
            "Products at or below their minimum level that still have stock (low), plus products with no stock left (out). " &
            "The line under it splits the two. Red when something is out of stock.",
            "Current stock of every product, and the minimum levels in Settings > Stock Threshold.")
        add(db, "Stock value (cost)",
            "For every product: stock on hand x cost price, added up (stock below 0 counts as 0). It is what the stock cost " &
            "the shop, not what it sells for. The line under it is how many products (sizes) exist.",
            "Current stock, and the cost price from each product's latest delivery.")
        add(db, "Deliveries, month",
            "Counts the deliveries sent (received into stock) with a delivery date since the 1st of this month. " &
            "The amount under it is their total cost.",
            "Delivery Management: deliveries with the status Sent.")
        add(db, "Draft deliveries",
            "Counts every delivery still in Draft, whatever its date. Their stock is not added until they are sent.",
            "Delivery Management: deliveries with the status Draft.")
        add(db, "Loyalty customers",
            "How many loyalty customers are registered. Under it: the points all customers hold together, and what they " &
            "are worth (points x the peso value of 1 point).",
            "File Maintenance > Customer Accounts, and the point value in the loyalty settings.")
        add(db, "Online now",
            "People whose app reported in during the last 30 seconds: they are logged in right now. Admins do not see the Superadmin.",
            "The log-in history (each running app reports in every 5 seconds).")

        ' ============================================================ Sales Report
        Dim sa As String = "Sales Report"
        add(sa, "Total sales",
            "Adds up the total of every receipt made in " & Period & ". It is what customers were charged after " &
            "discounts and special prices, VAT included. Points used to pay are part of the total." & CashierNote,
            "Every receipt saved by the POS (the TOTAL on the receipt).")
        add(sa, "Transactions",
            "Counts the receipts made in " & Period & ". One checkout at the POS is one transaction." & CashierNote,
            "Every receipt saved by the POS.")
        add(sa, "Items sold",
            "Adds the quantity of every item on those receipts. Example: 3 bolts and 2 rods on one receipt = 5 items." & CashierNote,
            "The item lines of every receipt in the period.")
        add(sa, "Average sale",
            "Total sales divided by Transactions, rounded to the centavo. Example: " & Peso & "1,000.00 from 4 receipts = " &
            Peso & "250.00.",
            "The Total sales and Transactions figures on this screen.")
        add(sa, "Profit (sales - cost)",
            "Total sales minus what the sold items cost the shop. The cost of an item = quantity x its cost price at the " &
            "moment it was sold (from the delivery it came in). A negative profit means the period was sold below cost " &
            "overall, usually because of a big special price or discount. Selling prices include VAT.",
            "Receipt totals, and the cost price saved on each item line when it was sold (filled in from Delivery Management).")
        add(sa, "Discounts given",
            "Adds the percentage discounts and the approved special prices taken off each receipt in " & Period & ". " &
            "Points are not included here; the Loss Report shows them.",
            "The DISCOUNT and SPECIAL PRICE amounts on every receipt.")
        add(sa, "Largest sale",
            "The highest receipt total in " & Period & ".",
            "Every receipt saved by the POS.")
        add(sa, "Sales in these categories",
            "Adds every item line sold in " & Period & ", grouped by the product's category. It can differ a little from " &
            "Total sales, because a discount on the whole receipt is not split over the categories.",
            "Item lines of every receipt, matched to the product and its category (Products).")
        add(sa, "Categories sold",
            "How many different categories had at least one item sold in " & Period & ".",
            "Item lines of every receipt, matched to Products > Categories.")
        add(sa, "Units sold",
            "Adds the quantity of every item sold in " & Period & ", in all categories.",
            "The item lines of every receipt in the period.")
        add(sa, "Best category",
            "The category with the highest sales in " & Period & ".",
            "The table below (sales per category).")
        add(sa, "Best category share",
            "Sales of the best category divided by Sales in these categories, times 100.",
            "The table below (sales per category).")

        ' ============================================================ Inventory Report
        Dim ir As String = "Inventory Report"
        add(ir, "Stock received",
            "Adds every piece that came into stock from deliveries sent in " & Period & ".",
            "The stock movement log: one 'Delivery received' line per item when a delivery is sent (Delivery Management).")
        add(ir, "Units sold",
            "Adds every piece that left stock through a sale in " & Period & ".",
            "The stock movement log: one 'Sale' line per item on each receipt (POS).")
        add(ir, "Adjustments (net)",
            "Manual stock corrections in " & Period & ": pieces added minus pieces removed.",
            "The stock movement log: the 'Adjustment' lines.")
        add(ir, "Net stock change",
            "Stock received - Units sold + Adjustments. Above 0 means there is more stock than at the " &
            "start of the period; below 0 means less.",
            "The other figures on this screen.")
        add(ir, "Products moved",
            "How many different products (each size counts) had at least one stock movement in " & Period & ".",
            "The stock movement log.")

        ' ============================================================ Delivery Report
        Dim dr As String = "Delivery Report"
        add(dr, "Deliveries received",
            "Counts the deliveries that were sent (received into stock) with a delivery date in " & Period & ". " &
            "Drafts are not counted.",
            "Delivery Management: deliveries with the status Sent.")
        add(dr, "Units received",
            "Adds the quantity (in pieces) of every item in those deliveries. Packs are counted as their pieces.",
            "The items of each sent delivery.")
        add(dr, "Total cost",
            "Adds the cost of those deliveries. The cost of a delivery = for every item, quantity x cost price per piece.",
            "The cost prices typed in Delivery Management when the delivery was made.")
        add(dr, "Expected profit",
            "For every item: (selling price - cost price per piece) x quantity, added up. It is what the shop earns if every " &
            "piece sells at its selling price; later discounts are not taken off.",
            "The cost and selling prices typed in Delivery Management.")
        add(dr, "Suppliers",
            "How many different suppliers (branches) delivered in " & Period & ".",
            "Sent deliveries and their supplier.")
        add(dr, "Drafts not yet sent",
            "Counts every delivery still in Draft, whatever its date. Their stock is not in the inventory until they are sent.",
            "Delivery Management: deliveries with the status Draft.")

        ' ============================================================ Loss Report
        Dim lr As String = "Loss Report"
        add(lr, "Total given up",
            "Discounts + Special prices + Points redeemed, for every receipt in " & Period & ".",
            "The three figures beside it (from the receipts saved by the POS).")
        add(lr, "Discounts",
            "Adds the percentage discounts (for example Senior Citizen 20%) taken off the receipts in " & Period & ".",
            "The DISCOUNT amount on every receipt (the discount picked at the POS).")
        add(lr, "Special prices",
            "Adds how much was taken off by special prices that an admin approved at the POS in " & Period & ".",
            "The SPECIAL PRICE amount on every receipt, with the approving admin's name.")
        add(lr, "Points redeemed",
            "Adds the peso value of the loyalty points customers used to pay in " & Period & ".",
            "The POINTS USED value on every receipt (Customer Accounts).")
        add(lr, "Sales with a concession",
            "Receipts that had a discount, a special price or points, out of all the receipts in " & Period & ".",
            "Every receipt saved by the POS.")
        add(lr, "Sold below cost",
            "Receipts where the money collected (the receipt total minus the points used) was less than what the items " &
            "cost the shop. The amount is how far below cost they went in total; the number in brackets is how many receipts.",
            "Receipt totals and points, and the cost price saved on each item line when it was sold.")
        add(lr, "Sales below cost",
            "How many receipts in " & Period & " collected less money than the items cost the shop.",
            "Receipt totals and points, and the cost price saved on each item line.")
        add(lr, "Total loss",
            "For each of those receipts: cost of the items - money collected (total minus points). Added up.",
            "The table below.")
        add(lr, "Biggest single loss",
            "The largest loss on one receipt in the table below.",
            "The table below.")
        add(lr, "Cost of those items",
            "Adds what the items on those receipts cost the shop (quantity x cost price).",
            "The cost price saved on each item line when it was sold.")

        ' ============================================================ Duty Records (User Monitoring)
        Dim du As String = "Duty Records"
        add(du, "People on duty",
            "How many different people were logged in at least once in " & Period & ".",
            "The log-in history: every log-in and log-out, recorded by the app on each computer.")
        add(du, "Total time logged in",
            "Adds each person's time per day, from log-in to log-out. If someone was logged in on two computers at once, " &
            "that time counts once. A session that goes past midnight is split between the two days.",
            "The log-in history (log-in time, log-out time, and the last time the app reported in).")
        add(du, "Average per person per day",
            "Total time logged in divided by the number of person-days (one person on one day = 1). " &
            "Example: 7 h 12 min over 2 person-days = 3 h 36 min.",
            "The Total time figure and the duty records below.")
        add(du, "Longest day",
            "The most time one person was logged in on a single day in " & Period & ".",
            "The duty records below.")
        add(du, "Sessions",
            "How many times someone logged in during " & Period & ". Every log-in is one session.",
            "The log-in history.")
        add(du, "No logout recorded",
            "Days where a session ended without Log out (the app was closed, crashed or the PC was switched off). " &
            "Those sessions are counted until the last time the app reported in (it does every 5 seconds).",
            "The log-in history.")

        ' ============================================================ Audit Trail
        Dim au As String = "Audit Trail"
        add(au, "Actions recorded",
            "Counts every entry in the audit trail in " & Period & ": log-ins, screens opened, sales, changes, deletions ...",
            "The audit trail: the app writes one entry every time someone does something.")
        add(au, "People active",
            "How many different accounts have at least one entry in " & Period & ".",
            "The audit trail.")
        add(au, "Logins",
            "Counts the successful log-ins in " & Period & ".",
            "The audit trail: 'Logged in' entries.")
        add(au, "Failed logins",
            "Counts the log-ins refused because of a wrong username or password in " & Period & ". " &
            "Five wrong passwords in a row lock an account for 15 minutes.",
            "The audit trail: 'Failed login' entries.")
        add(au, "Deletions",
            "Counts the records deleted in " & Period & " (users, products, suppliers, discounts ...).",
            "The audit trail: 'Deleted' entries.")
        add(au, "Price overrides",
            "Counts special prices approved at the POS, price changes, and failed override attempts in " & Period & ".",
            "The audit trail: 'Special price', 'Price change' and 'Failed override' entries.")

        ' ============================================================ Reports > Inventory (current stock)
        Dim inv As String = "Inventory"
        add(inv, "Items listed",
            "Counts the products (each size counts) shown in the list right now. The search and filters change it.",
            "Products, with their current stock.")
        add(inv, "Stock value (at cost)",
            "For every product in the list: stock on hand x cost price. Added up. It is what the stock cost the shop, " &
            "not what it sells for.",
            "Current stock (deliveries in, sales out) and the cost price from the latest delivery.")
        add(inv, "Low stock",
            "Products in the list that still have stock, but at or below their minimum level. Products without a " &
            "minimum level are never low.",
            "Current stock, and the minimum levels in Settings > Stock Threshold.")
        add(inv, "Out of stock",
            "Products in the list with no stock left (0 or less).",
            "Current stock: deliveries in, minus sales.")
        add(inv, "VAT in the prices",
            "The VAT rate the system uses. Selling prices already include it; it is only used to show how much of a price is VAT.",
            "Settings > VAT.")

        ' ============================================================ User Monitoring (live)
        Dim um As String = "User Monitoring"
        Const umSearch As String = " Only the people matching the search box are counted."
        add(um, "People logged in",
            "People whose app reported in during the last 30 seconds (every running app does every 5 seconds)." & umSearch,
            "The log-in history: sessions without a log-out and with a fresh report.")
        add(um, "PCs in use",
            "How many different computers those people are logged in on." & umSearch,
            "The computer name each app sends when it logs in.")
        add(um, "Admins online",
            "People logged in right now with the Admin role." & umSearch,
            "The live sessions and the role in File Maintenance > Users.")
        add(um, "Cashiers online",
            "People logged in right now with the Cashier role." & umSearch,
            "The live sessions and the role in File Maintenance > Users.")

        ' ============================================================ File Maintenance > Users
        Dim us As String = "Users"
        add(us, "Superadmin",
            "Accounts with the Superadmin role / the most allowed (1).",
            "File Maintenance > Users.")
        add(us, "Admins",
            "Accounts with the Admin role / the most allowed (" & UserRules.MaxAdmins & "). Red when the limit is reached.",
            "File Maintenance > Users.")
        add(us, "Cashiers",
            "Accounts with the Cashier role / the most allowed (" & UserRules.MaxCashiers & "). Red when the limit is reached.",
            "File Maintenance > Users.")
        add(us, "Active now",
            "People whose app reported in during the last 30 seconds: they are logged in right now.",
            "The log-in history (each running app reports in every 5 seconds).")
        add(us, "Locked accounts",
            "Accounts locked by an administrator, plus accounts locked for 15 minutes after 5 wrong passwords in a row.",
            "File Maintenance > Users (Unlock account clears it).")

        ' ============================================================ File Maintenance > Products
        Dim pr As String = "Products"
        add(pr, "Products",
            "Different product names. The same name in the same category counts once, however many sizes it has.",
            "File Maintenance > Products.")
        add(pr, "Sizes in total",
            "Every product row: each size of a product counts once (welding rod 6inch and welding rod 8inch = 2).",
            "File Maintenance > Products.")
        add(pr, "Categories",
            "How many categories exist, with or without products.",
            "File Maintenance > Products > Categories.")

        ' ============================================================ File Maintenance > Suppliers
        Dim sp As String = "Suppliers"
        add(sp, "Suppliers (branches)",
            "Every supplier entry. One company with two branches counts 2.",
            "File Maintenance > Suppliers.")
        add(sp, "Companies",
            "Different company names among the suppliers. One company with two branches counts 1.",
            "File Maintenance > Suppliers.")

        ' ============================================================ File Maintenance > Customer Accounts
        Dim cu As String = "Customer Accounts"
        add(cu, "Members",
            "How many loyalty customers are registered.",
            "File Maintenance > Customer Accounts.")
        add(cu, "Points held",
            "Adds every customer's current points balance: points earned from purchases minus points used to pay.",
            "Each customer's points history (earned at the POS, used at the POS, adjustments).")
        add(cu, "New this month",
            "Customers registered since the 1st of this month.",
            "File Maintenance > Customer Accounts (the date each card was registered).")

        ' ============================================================ File Maintenance > Discounts
        Dim di As String = "Discounts"
        add(di, "Discounts",
            "Every discount, active or not.",
            "File Maintenance > Discounts.")
        add(di, "Active",
            "Discounts the cashier can pick at the POS right now. Deactivated ones are kept for the past sales.",
            "File Maintenance > Discounts.")

        ' ============================================================ File Maintenance > Payment Methods
        Dim pm As String = "Payment Methods"
        add(pm, "Payment methods",
            "Every payment method, active or not, Cash included.",
            "File Maintenance > Payment Methods.")
        add(pm, "Active",
            "Payment methods the cashier can choose at the POS right now. Cash is always active.",
            "File Maintenance > Payment Methods.")

        ' ============================================================ POS
        Dim po As String = "POS"
        add(po, "My transactions (24 h)",
            "Your receipts in the last 24 hours (not since midnight: exactly the last 24 hours).",
            "Every receipt saved by the POS under your name.")
        add(po, "My total sales (24 h)",
            "Adds the totals of your receipts in the last 24 hours.",
            "Every receipt saved by the POS under your name.")

        Return d
    End Function

End Module
