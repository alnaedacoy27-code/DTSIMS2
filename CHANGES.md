# Changes added to DTSIMS2

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
