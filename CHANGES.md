# Changes added to DTSIMS2

## Database Settings, Permission Maintenance and validation (schema version 7)

The app now needs schema version 7. On a version 6 database it **upgrades itself at startup** (adds the
`user_permissions` table, a `pre_reset` backup type and role rows for every module). If that ever fails,
run `Database/07_permissions_and_reset.sql` in MySQL Workbench.
For a fresh database: import `dtsims2_db.sql`, then run `Database/06_sms_queue.sql` (the app does 7 by itself).

### Settings > Database Settings (Superadmin only)
- **Backup folder**: change it (the Windows folder picker can also make a folder), *New folder* (name + where), *Open folder*.
- **Create backup**: the whole database into one `.sql` file (no mysqldump needed), with an optional note.
  A consistent snapshot, so other laptops can keep selling while it runs.
- **Saved backups** list: *Restore*, *Restore from file...*, *Delete* (to the Recycle Bin), *Refresh*.
- **Restore** always saves a backup of the current system first ("Before restore"), replaces the data in one
  transaction (works completely or changes nothing) and logs everyone out. Not restored: the backup history,
  queued text messages (old ones would be sent again) and the live-update log.
- **Reset system**: full reset, or chosen parts (sales, deliveries, stock, products, suppliers, customers, discounts,
  admins and cashiers, activity logs). Parts that others depend on are added automatically. A backup is made first
  ("Before reset"). Always kept: the Superadmin, VAT, settings, permissions and the backups.
- Every action asks for **two steps**: the Superadmin's password (3 tries, failures audited) and a typed word
  (BACKUP, RESTORE, DELETE, RESET).
- Backup files end with a SHA-256 checksum: an edited, damaged or half-copied file is refused. A restore only runs the
  statement shapes the app writes, one statement per command (a smuggled second statement cannot run).
- The main window will not switch screens, log out or close while a backup, restore or reset is running.

### Settings > Permission Maintenance (Superadmin only)
- **By role**: a switch per module for Admin and Cashier. Every person with that role follows it.
- **By user**: per person, per module: *Follow role*, *Allow* or *Deny*. A person's own setting wins over the role.
- Database Settings and Permission Maintenance are Superadmin-only and can never be given away.
- Saved at once; every laptop updates its menu within seconds (and leaves a screen it may no longer open).
- If the tables cannot be read, the built-in defaults apply, so nobody is locked out.

### DT icon
- New `DT icon.ico`: a white DT monogram on the app's maroon (the same colors as `Theme`), drawn separately for every
  size from 16 to 256 px so it stays sharp in the title bar and taskbar. It is the exe icon and is embedded
  (`DTSIMS2.AppIcon.ico`) so every window shows it. The old `DT logo.ico` / `dtsims logo.ico` are no longer used.
- The login, setup and connection screens now get the maroon title bar too, and show the DT mark above the name.

### Home > Menu / Dashboard
- A switch at the top right of the home screen; the choice is remembered on each computer.
- Dashboard, row 1 (selling): sales today (vs yesterday), transactions and average sale, items sold, sales this month
  (vs the same days last month), gross profit this month (% of sales), given away this month (discounts, special
  prices, points).
- Row 2 (operations): low / out of stock, stock value at cost, deliveries received this month (and their cost),
  draft deliveries not yet added to stock, stock losses this month (damaged / lost, from `loss_records`),
  loyalty customers and the points they hold.
- Sales and gross profit for the last 7 days, the top 5 products, who is online; then recent sales, products to
  restock, recent deliveries, and recent discounts and losses, each with an Open link.
- Each part only shows when the person may open the matching module; a Cashier sees only their own sales and never
  costs or profit.
  It loads in the background and refreshes when sales, stock or deliveries change, and every minute.

### "?" on every figure
- Every figure tile on the Dashboard, the reports (Sales, Inventory, Delivery, Loss, Duty Records, Audit Trail),
  Inventory, User Monitoring, Users, Products, Suppliers, Customer Accounts, Discounts and the POS has a small "?".
  Clicking it shows how the number is calculated and where it comes from.
- All the wording is in `Modules/FigureHelp.vb` (one place to edit). A tile whose caption is not listed there has no "?".

### Menu
- Customer Accounts and Discounts moved to File Maintenance (their permissions are unchanged).

### Utilities > Audit Trail (Superadmin only)
- A read-only record of every action: who did what, on which screen, on which computer, and when.
- Views: *Activity log* (every entry, searchable), *Summary* (actions by type and per day), *By person*.
- Daily / Weekly / Monthly, Save PDF / Excel, Print. New entries appear by themselves within about 10 seconds.
- Like Database Settings and Permission Maintenance it can never be given to an Admin or Cashier.

### Validation
- Names (people, products, companies, categories, discounts) are at most **100** characters; addresses 150;
  search and free-text boxes 100 (category description stays 255).
- Stricter email check (no double dots, leading/trailing dots or bad domains).
- Names and addresses must contain letters or numbers (not only punctuation). Too-long pasted values are caught.
- Connection screen: port is numbers only (1 to 65535); server, database, user and password have length limits.
- Devices: the balance-check number, word and USSD code have limits and only accept fitting characters.

### Security
- The Debug-only Ctrl+Shift+R role preview now works only for an account that really is the Superadmin in the
  database. Before, any cashier using a Debug build (the app is published from `bin\Debug`) could press it to get
  the Superadmin menus.

### Look
- Module headers are 20 px shorter (title, subtitle, search and buttons moved up) on every screen.
- VAT: the explanation sits beside the card on wide windows instead of below it.

---

No database changes: every new screen reads tables that already exist (schema version 6).
`dtsims2_db.sql` is still the old version 5 dump: for a fresh database, import it and then run `Database/06_sms_queue.sql`.
Settings that belong to one computer are saved in `%APPDATA%\DTSIMS2\` (as before).

## Display
- **Text size** (Settings > Text Size): Default, Medium or Large, per computer, applied after a restart.
  `Theme.FontScale` / `Theme.LayoutScale` / `Theme.S()` scale text and container sizes together.
  Display numbers of 24 pt and up keep their designed size.
- Darker text colours, home tiles and POS product tiles that fill the width, scroll bars when the window is smaller than needed.

## Speed
- The modem port scan (about 1-2 s per COM port) no longer runs on the screen thread or on every refresh: it runs once in the
  background at startup and from Devices > check. `SmsService.StatusText` / `HasModem` never scan.
- Login no longer does a DNS lookup (`SessionTracker.LocalIp` reads the network adapters).
- "localhost" connects as 127.0.0.1.
- Optional timing log: create an empty file `%APPDATA%\DTSIMS2\perf.enable` and read `perf.log` next to it (`PerfLog.Mark`).

## Reports (Reports menu)
One shared screen (`ReportFormBase`): Daily / Weekly / Monthly, date button with calendar, status figures, bar charts beside the table,
search, Save PDF, Save Excel (.xlsx), Print (asks which printer; only A4 / Letter / Legal paper, never a receipt roll).
- **Sales Report**: summary (top 10 products, sales per hour/day), Receipts (View receipt, Print copy stamped REPRINT / COPY, audited), Categories.
- **Inventory Report**: stock received / sold / lost / adjusted, movement log.
- **Delivery Report** (new): deliveries and cost by supplier, every delivery with its items.
- **Loss Report**: discounts, special prices and points given up; the sales behind it; per cashier and approver; sales below cost.
- **User Monitoring** moved from Utilities to Reports: Live monitoring | **Duty records** (daily time record per person, per-person totals,
  every session), built from `user_sessions`.

## Cleanup
- Removed the duplicate folder `Forms/File Maintenance` (the project uses `Forms/FileMaintenance`).

## New files
Modules: `ReportModel`, `ReportCanvas`, `ReportExport`, `ReportData` (also holds `LossData`), `DutyData`, `TextSize`, `PerfLog`.
Controls: `BarChart`, `PillTabs`, `DateChip`.
Forms: `Reports/ReportFormBase`, `SalesReportForm`, `InventoryReportForm`, `DeliveryReportForm`, `LossReportForm`, `ReceiptCopyDialog`,
`SystemUtilities/DutyRecordsForm`, `Settings/TextSizeForm`.
`DTSIMS2.vbproj` also references `System.IO.Compression` (used to write .xlsx).
