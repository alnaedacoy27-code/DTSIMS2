Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports MySql.Data.MySqlClient

''' <summary>What a backup file says about itself (read from its first lines).</summary>
Public Class BackupInfo
    Public Property FilePath As String = ""
    Public Property FileName As String = ""
    Public Property SizeBytes As Long
    Public Property CreatedAt As DateTime
    ''' <summary>manual, pre_restore or pre_reset</summary>
    Public Property Kind As String = "manual"
    Public Property CreatedBy As String = ""
    Public Property Note As String = ""
    Public Property DatabaseName As String = ""
    Public Property SchemaVersion As Integer
    Public Property TableCount As Integer
    Public Property RowCount As Long
    Public Property IsBackup As Boolean

    Public ReadOnly Property KindDisplay As String
        Get
            Select Case Kind
                Case "pre_restore"
                    Return "Before restore"
                Case "pre_reset"
                    Return "Before reset"
                Case "auto"
                    Return "Automatic"
                Case Else
                    Return "Manual"
            End Select
        End Get
    End Property

    Public ReadOnly Property SizeDisplay As String
        Get
            If SizeBytes < 1024L * 1024L Then Return Math.Max(1L, SizeBytes \ 1024L).ToString("N0") & " KB"
            Return (SizeBytes / (1024.0 * 1024.0)).ToString("0.0") & " MB"
        End Get
    End Property
End Class

''' <summary>A backup or restore could not be done; the message is safe to show.</summary>
Public Class BackupException
    Inherits Exception
    Public Sub New(message As String)
        MyBase.New(message)
    End Sub
End Class

''' <summary>
''' Backups of the whole database as one .sql file, written by the app itself (no mysqldump needed).
''' The file can also be run in MySQL Workbench. Layout:
'''   -- DTSIMS2 BACKUP                       first line (signature)
'''   -- @@key: value                          details (date, kind, who, schema version ...)
'''   -- @@TABLE `name`                        then CREATE TABLE IF NOT EXISTS, DELETE FROM, INSERT INTO ... per table
'''   -- @@TRIGGERS                            the triggers (for Workbench only; the app keeps the ones it has)
'''   -- @@CHECKSUM sha256                     last line: proves the file is complete and unchanged
''' A restore checks the checksum, only runs the statement shapes above (one statement at a time),
''' and replaces the data inside one transaction: it works completely or changes nothing.
''' </summary>
Public Module BackupService

    Public Const FileSignature As String = "-- DTSIMS2 BACKUP"
    Private Const MetaPrefix As String = "-- @@"
    Private Const TableMarker As String = "-- @@TABLE "
    Private Const TriggersMarker As String = "-- @@TRIGGERS"
    Private Const ChecksumMarker As String = "-- @@CHECKSUM "
    Private Const FormatVersion As Integer = 1
    Private Const BatchChars As Integer = 512 * 1024

    ''' <summary>Data in these tables is never replaced by a restore: the backup history, the live-update log,
    ''' queued text messages (old ones would be sent again) and the schema version.</summary>
    Private ReadOnly KeepOnRestore As String() = {"backup_log", "change_log", "sms_queue", "schema_version"}

    ''' <summary>Structure only: the live-update log is not worth saving.</summary>
    Private ReadOnly StructureOnly As String() = {"change_log"}

    Private ReadOnly SafeName As New Regex("^[A-Za-z0-9_]{1,64}$")
    Private ReadOnly Inv As CultureInfo = CultureInfo.InvariantCulture

    Private _busy As Integer

    ''' <summary>True while a backup, restore or reset runs (the main window must not close or switch screens).</summary>
    Public ReadOnly Property IsBusy As Boolean
        Get
            Return Volatile.Read(_busy) = 1
        End Get
    End Property

    ''' <summary>Marks the start of a long job. False when one is already running.</summary>
    Public Function TryBegin() As Boolean
        Return Interlocked.CompareExchange(_busy, 1, 0) = 0
    End Function

    Public Sub EndJob()
        Interlocked.Exchange(_busy, 0)
    End Sub

    ' ================================================================== backup folder
    ''' <summary>The folder from Settings (shared by every laptop); a default under Documents when none is set.</summary>
    Public Function GetFolder() As String
        Dim v As String = ""
        Try
            Dim o As Object = Db.Scalar("SELECT setting_value FROM settings WHERE setting_key = 'backup_folder'")
            If o IsNot Nothing AndAlso o IsNot DBNull.Value Then v = o.ToString().Trim()
        Catch
            v = ""
        End Try
        If v.Length = 0 Then
            v = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DTSIMS2 Backups")
        End If
        Return v.Replace("/"c, "\"c)
    End Function

    Public Sub SetFolder(folder As String)
        If String.IsNullOrWhiteSpace(folder) OrElse folder.Length > 400 Then Throw New BackupException("Choose a folder with a shorter path.")
        Db.NonQuery("INSERT INTO settings (setting_key, setting_value) VALUES ('backup_folder', @v) " &
                    "ON DUPLICATE KEY UPDATE setting_value = @v", Db.Params("@v", folder.Trim()))
    End Sub

    ''' <summary>"" when the name can be a folder name, otherwise why not.</summary>
    Public Function FolderNameProblem(name As String) As String
        Dim n As String = If(name, "").Trim()
        If n.Length = 0 Then Return "Type a name for the new folder."
        If n.Length > 60 Then Return "The folder name can be at most 60 characters."
        If n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 Then Return "A folder name cannot contain \ / : * ? "" < > |"
        If n.EndsWith(".") OrElse n.StartsWith(".") Then Return "A folder name cannot start or end with a dot."
        Dim reserved As String() = {"CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "LPT1", "LPT2", "LPT3"}
        If Array.IndexOf(reserved, n.ToUpperInvariant()) >= 0 Then Return "Windows does not allow that folder name."
        Return ""
    End Function

    ' ================================================================== list / read
    ''' <summary>Every DTSIMS2 backup in the folder, newest first. Other .sql files are ignored.</summary>
    Public Function ListBackups(folder As String) As List(Of BackupInfo)
        Dim result As New List(Of BackupInfo)()
        If String.IsNullOrWhiteSpace(folder) OrElse Not Directory.Exists(folder) Then Return result
        For Each f As String In Directory.GetFiles(folder, "*.sql", SearchOption.TopDirectoryOnly)
            Dim info As BackupInfo = ReadInfo(f)
            If info.IsBackup Then result.Add(info)
        Next
        result.Sort(Function(a, b) b.CreatedAt.CompareTo(a.CreatedAt))
        Return result
    End Function

    ''' <summary>Reads the details at the top of a file. IsBackup = False when it is not a DTSIMS2 backup.</summary>
    Public Function ReadInfo(file As String) As BackupInfo
        Dim info As New BackupInfo()
        info.FilePath = file
        info.FileName = Path.GetFileName(file)
        Try
            Dim fi As New FileInfo(file)
            info.SizeBytes = fi.Length
            info.CreatedAt = fi.LastWriteTime
            Using sr As New StreamReader(file, New UTF8Encoding(False), False)
                If sr.ReadLine() <> FileSignature Then Return info
                For i As Integer = 1 To 30
                    Dim line As String = sr.ReadLine()
                    If line Is Nothing OrElse Not line.StartsWith(MetaPrefix) Then Exit For
                    Dim colon As Integer = line.IndexOf(":"c)
                    If colon < 0 Then Continue For
                    Dim key As String = line.Substring(MetaPrefix.Length, colon - MetaPrefix.Length).Trim().ToLowerInvariant()
                    Dim value As String = line.Substring(colon + 1).Trim()
                    Select Case key
                        Case "created"
                            Dim d As DateTime
                            If DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", Inv, DateTimeStyles.None, d) Then info.CreatedAt = d
                        Case "kind"
                            info.Kind = value
                        Case "by"
                            info.CreatedBy = value
                        Case "note"
                            info.Note = value
                        Case "database"
                            info.DatabaseName = value
                        Case "schema"
                            Integer.TryParse(value, info.SchemaVersion)
                        Case "tables"
                            Integer.TryParse(value, info.TableCount)
                        Case "rows"
                            Long.TryParse(value, info.RowCount)
                    End Select
                Next
            End Using
            info.IsBackup = True
        Catch
            info.IsBackup = False
        End Try
        Return info
    End Function

    ' ================================================================== create
    ''' <summary>Saves the whole database into a new file in <paramref name="folder"/>. Returns the new backup.</summary>
    Public Function CreateBackup(folder As String, kind As String, note As String, progress As Action(Of String)) As BackupInfo
        If String.IsNullOrWhiteSpace(folder) Then Throw New BackupException("Choose a backup folder first.")
        Try
            Directory.CreateDirectory(folder)
        Catch ex As Exception
            Throw New BackupException("Cannot use the backup folder " & folder & ": " & ex.Message)
        End Try

        Dim stamp As DateTime = DateTime.Now
        Dim suffix As String = If(kind = "manual", "", "_" & kind.Replace("pre_", "before-"))
        Dim name As String = "DTSIMS2_" & stamp.ToString("yyyy-MM-dd_HHmmss", Inv) & suffix & ".sql"
        Dim full As String = Path.Combine(folder, name)
        Dim n As Integer = 2
        While File.Exists(full)
            full = Path.Combine(folder, Path.GetFileNameWithoutExtension(name) & "_" & n & ".sql")
            n += 1
        End While
        Dim temp As String = full & ".part"

        Try
            Using conn As MySqlConnection = Db.OpenConnection()
                ' every table as it was at one moment, even while other laptops keep selling
                Exec(conn, "SET SESSION TRANSACTION ISOLATION LEVEL REPEATABLE READ")
                Exec(conn, "START TRANSACTION WITH CONSISTENT SNAPSHOT")

                Dim tables As List(Of String) = BaseTables(conn)
                Dim counts As New Dictionary(Of String, Long)()
                Dim total As Long = 0
                For Each t As String In tables
                    Dim c As Long = If(Array.IndexOf(StructureOnly, t) >= 0, 0L,
                                       Convert.ToInt64(ScalarOn(conn, "SELECT COUNT(*) FROM " & Q(t))))
                    counts(t) = c
                    total += c
                Next

                Using fs As New FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16)
                    Using w As New HashedLineWriter(fs)
                        w.Line(FileSignature)
                        w.Line(MetaPrefix & "format: " & FormatVersion)
                        w.Line(MetaPrefix & "created: " & stamp.ToString("yyyy-MM-dd HH:mm:ss", Inv))
                        w.Line(MetaPrefix & "kind: " & kind)
                        w.Line(MetaPrefix & "by: " & OneLine(AppSession.FullName & " (" & AppSession.Username & ")"))
                        w.Line(MetaPrefix & "note: " & OneLine(note))
                        w.Line(MetaPrefix & "database: " & conn.Database)
                        ' the version this database really has (not the one this app needs: an older database is backed up before its upgrade)
                        w.Line(MetaPrefix & "schema: " & Convert.ToInt32(ScalarOn(conn, "SELECT COALESCE(MAX(version), 0) FROM schema_version")))
                        w.Line(MetaPrefix & "tables: " & tables.Count)
                        w.Line(MetaPrefix & "rows: " & total)
                        w.Line(MetaPrefix & "computer: " & OneLine(Environment.MachineName))
                        w.Line("--")
                        w.Line("-- Restore it from the DTSIMS2 app: Settings > Database Settings > Restore.")
                        w.Line("-- Do not edit this file: the last line is a checksum and an edited file is refused.")
                        w.Line("-- It holds every record, including password hashes: keep it somewhere safe.")
                        w.Line("SET NAMES utf8mb4;")
                        w.Line("SET FOREIGN_KEY_CHECKS = 0;")
                        w.Line("SET UNIQUE_CHECKS = 0;")

                        Dim i As Integer = 0
                        For Each t As String In tables
                            i += 1
                            If progress IsNot Nothing Then progress("Saving " & t & " (" & i & " of " & tables.Count & ")...")
                            w.Line(TableMarker & Q(t) & " rows=" & counts(t))
                            w.Line(CreateStatement(conn, t) & ";")
                            w.Line("DELETE FROM " & Q(t) & ";")
                            If Array.IndexOf(StructureOnly, t) < 0 Then DumpRows(conn, t, w)
                        Next

                        w.Line("SET UNIQUE_CHECKS = 1;")
                        w.Line("SET FOREIGN_KEY_CHECKS = 1;")
                        WriteTriggers(conn, w)
                        w.Finish()
                    End Using
                End Using
                Exec(conn, "COMMIT")
            End Using

            File.Move(temp, full)
        Catch ex As BackupException
            TryDelete(temp)
            Throw
        Catch ex As Exception
            TryDelete(temp)
            Throw New BackupException("The backup could not be made: " & ex.Message)
        End Try

        Dim info As BackupInfo = ReadInfo(full)
        Try
            Db.NonQuery("INSERT INTO backup_log (file_name, file_path, file_size, backup_type, created_by, note) " &
                        "VALUES (@n, @p, @s, @k, @u, @note)",
                        Db.Params("@n", Left(info.FileName, 200), "@p", Left(full, 500), "@s", info.SizeBytes, "@k", kind,
                                  "@u", If(AppSession.UserId > 0, CObj(AppSession.UserId), Nothing),
                                  "@note", If(String.IsNullOrWhiteSpace(note), Nothing, Left(note.Trim(), 200))))
        Catch
            ' the file is what matters
        End Try
        Return info
    End Function

    Private Function BaseTables(conn As MySqlConnection) As List(Of String)
        Dim l As New List(Of String)()
        Using cmd As New MySqlCommand("SELECT table_name FROM information_schema.tables " &
                                      "WHERE table_schema = DATABASE() AND table_type = 'BASE TABLE' ORDER BY table_name", conn)
            Using r As MySqlDataReader = cmd.ExecuteReader()
                While r.Read()
                    Dim t As String = r.GetString(0)
                    If SafeName.IsMatch(t) Then l.Add(t)
                End While
            End Using
        End Using
        Return l
    End Function

    ''' <summary>SHOW CREATE TABLE as one line, with IF NOT EXISTS and without the AUTO_INCREMENT counter.</summary>
    Private Function CreateStatement(conn As MySqlConnection, table As String) As String
        Dim sql As String
        Using cmd As New MySqlCommand("SHOW CREATE TABLE " & Q(table), conn)
            Using r As MySqlDataReader = cmd.ExecuteReader()
                r.Read()
                sql = r.GetString(1)
            End Using
        End Using
        sql = Regex.Replace(sql, "\s*[\r\n]+\s*", " ")
        sql = Regex.Replace(sql, "\sAUTO_INCREMENT=\d+", "")
        Dim prefix As String = "CREATE TABLE " & Q(table)
        If sql.StartsWith(prefix) Then sql = "CREATE TABLE IF NOT EXISTS " & Q(table) & sql.Substring(prefix.Length)
        Return sql
    End Function

    Private Sub DumpRows(conn As MySqlConnection, table As String, w As HashedLineWriter)
        Using cmd As New MySqlCommand("SELECT * FROM " & Q(table), conn)
            cmd.CommandTimeout = 0
            Using r As MySqlDataReader = cmd.ExecuteReader()
                Dim cols As New List(Of String)()
                For i As Integer = 0 To r.FieldCount - 1
                    cols.Add(Q(r.GetName(i)))
                Next
                Dim prefix As String = "INSERT INTO " & Q(table) & " (" & String.Join(", ", cols.ToArray()) & ") VALUES "
                Dim sb As New StringBuilder()
                While r.Read()
                    sb.Append(If(sb.Length = 0, prefix, ","))
                    sb.Append("("c)
                    For i As Integer = 0 To r.FieldCount - 1
                        If i > 0 Then sb.Append(","c)
                        sb.Append(Literal(r, i))
                    Next
                    sb.Append(")"c)
                    If sb.Length >= BatchChars Then
                        w.Line(sb.Append(";"c).ToString())
                        sb.Clear()
                    End If
                End While
                If sb.Length > 0 Then w.Line(sb.Append(";"c).ToString())
            End Using
        End Using
    End Sub

    ''' <summary>One value as SQL text. Never contains a line break.</summary>
    Private Function Literal(r As MySqlDataReader, i As Integer) As String
        If r.IsDBNull(i) Then Return "NULL"
        Dim v As Object = r.GetValue(i)
        If TypeOf v Is Byte() Then
            Dim b As Byte() = DirectCast(v, Byte())
            If b.Length = 0 Then Return "''"
            Return "0x" & ToHex(b)
        End If
        If TypeOf v Is String Then Return "'" & Escape(DirectCast(v, String)) & "'"
        If TypeOf v Is DateTime Then Return "'" & DirectCast(v, DateTime).ToString("yyyy-MM-dd HH:mm:ss", Inv) & "'"
        If TypeOf v Is Boolean Then Return If(DirectCast(v, Boolean), "1", "0")
        If TypeOf v Is TimeSpan Then Return "'" & DirectCast(v, TimeSpan).ToString() & "'"
        If TypeOf v Is Double Then Return DirectCast(v, Double).ToString("R", Inv)
        If TypeOf v Is Single Then Return DirectCast(v, Single).ToString("R", Inv)
        If TypeOf v Is Decimal OrElse TypeOf v Is Integer OrElse TypeOf v Is Long OrElse TypeOf v Is Short OrElse
           TypeOf v Is Byte OrElse TypeOf v Is SByte OrElse TypeOf v Is UInteger OrElse TypeOf v Is ULong OrElse TypeOf v Is UShort Then
            Return Convert.ToString(v, Inv)
        End If
        If TypeOf v Is MySql.Data.Types.MySqlDateTime Then
            Dim m As MySql.Data.Types.MySqlDateTime = DirectCast(v, MySql.Data.Types.MySqlDateTime)
            If m.IsValidDateTime Then Return "'" & m.GetDateTime().ToString("yyyy-MM-dd HH:mm:ss", Inv) & "'"
            Return "NULL"
        End If
        Return "'" & Escape(Convert.ToString(v, Inv)) & "'"
    End Function

    Private Function Escape(s As String) As String
        Dim sb As New StringBuilder(s.Length + 8)
        For Each c As Char In s
            Select Case c
                Case "\"c
                    sb.Append("\\")
                Case "'"c
                    sb.Append("\'")
                Case ChrW(0)
                    sb.Append("\0")
                Case ChrW(10)
                    sb.Append("\n")
                Case ChrW(13)
                    sb.Append("\r")
                Case ChrW(26)
                    sb.Append("\Z")
                Case Else
                    sb.Append(c)
            End Select
        Next
        Return sb.ToString()
    End Function

    Private Sub WriteTriggers(conn As MySqlConnection, w As HashedLineWriter)
        Dim dt As New DataTable()
        Using cmd As New MySqlCommand("SELECT trigger_name, action_timing, event_manipulation, event_object_table, action_statement " &
                                      "FROM information_schema.triggers WHERE trigger_schema = DATABASE() ORDER BY trigger_name", conn)
            Using a As New MySqlDataAdapter(cmd)
                a.Fill(dt)
            End Using
        End Using
        w.Line(TriggersMarker)
        w.Line("-- MySQL Workbench only (needs an account that may create triggers). The app keeps the triggers it already has.")
        If dt.Rows.Count = 0 Then Return
        w.Line("DELIMITER $$")
        For Each row As DataRow In dt.Rows
            Dim nm As String = row("trigger_name").ToString()
            Dim tbl As String = row("event_object_table").ToString()
            If Not SafeName.IsMatch(nm) OrElse Not SafeName.IsMatch(tbl) Then Continue For
            w.Line("DROP TRIGGER IF EXISTS " & Q(nm) & "$$")
            w.Line("CREATE TRIGGER " & Q(nm) & " " & row("action_timing").ToString() & " " & row("event_manipulation").ToString() &
                   " ON " & Q(tbl) & " FOR EACH ROW " & OneLine(row("action_statement").ToString()) & "$$")
        Next
        w.Line("DELIMITER ;")
    End Sub

    ' ================================================================== check
    ''' <summary>"" when the file is a complete, unchanged DTSIMS2 backup this app can restore; otherwise why not.</summary>
    Public Function ValidateForRestore(file As String) As String
        If Not System.IO.File.Exists(file) Then Return "The backup file no longer exists."
        Dim info As BackupInfo = ReadInfo(file)
        If Not info.IsBackup Then Return "This is not a DTSIMS2 backup file."
        If info.SchemaVersion > Db.RequiredSchemaVersion Then
            Return "This backup was made by a newer version of the app (database version " & info.SchemaVersion & "). Update the app first."
        End If
        If info.SchemaVersion < 6 Then Return "This backup is too old to restore with this version of the app."
        If Not ChecksumOk(file) Then Return "The backup file is incomplete or was changed after it was made. It cannot be restored."
        Return ""
    End Function

    Private Function ChecksumOk(file As String) As Boolean
        Dim tailLen As Integer = ChecksumMarker.Length + 64 + 1
        Using fs As New FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16)
            If fs.Length <= tailLen Then Return False
            fs.Seek(-tailLen, SeekOrigin.End)
            Dim tail(tailLen - 1) As Byte
            Dim got As Integer = 0
            While got < tailLen
                Dim k As Integer = fs.Read(tail, got, tailLen - got)
                If k <= 0 Then Return False
                got += k
            End While
            Dim t As String = Encoding.ASCII.GetString(tail)
            If Not t.StartsWith(ChecksumMarker) OrElse t(t.Length - 1) <> ChrW(10) Then Return False
            Dim expected As String = t.Substring(ChecksumMarker.Length, 64)

            fs.Seek(0, SeekOrigin.Begin)
            Using sha As SHA256 = SHA256.Create()
                Dim remaining As Long = fs.Length - tailLen
                Dim buf(65535) As Byte
                While remaining > 0
                    Dim k As Integer = fs.Read(buf, 0, CInt(Math.Min(buf.Length, remaining)))
                    If k <= 0 Then Return False
                    sha.TransformBlock(buf, 0, k, Nothing, 0)
                    remaining -= k
                End While
                sha.TransformFinalBlock(New Byte() {}, 0, 0)
                Return String.Equals(ToHex(sha.Hash), expected, StringComparison.OrdinalIgnoreCase)
            End Using
        End Using
    End Function

    ' ================================================================== restore
    ''' <summary>
    ''' Replaces the data with the backup's. Run CreateBackup first (the screen does).
    ''' All-or-nothing: if any statement fails, the database stays as it was.
    ''' </summary>
    Public Sub Restore(file As String, progress As Action(Of String))
        Dim problem As String = ValidateForRestore(file)
        If problem.Length > 0 Then Throw New BackupException(problem)

        Try
            Using conn As New MySqlConnection(RestoreConnectionString())
                conn.Open()

                Exec(conn, "SET FOREIGN_KEY_CHECKS = 0")
                Exec(conn, "SET UNIQUE_CHECKS = 0")

                ' 1. tables the backup has but this database lacks (DDL cannot be inside the transaction)
                If progress IsNot Nothing Then progress("Checking the tables...")
                Dim backupTables As New List(Of String)()
                ReadStatements(file,
                    Sub(table As String, sql As String)
                        If Not backupTables.Contains(table) Then backupTables.Add(table)
                        If sql.StartsWith("CREATE TABLE IF NOT EXISTS " & Q(table) & " (") Then Exec(conn, sql)
                    End Sub)

                Dim dbTables As List(Of String) = BaseTables(conn)

                ' 2. the data, in one transaction
                Using tx As MySqlTransaction = conn.BeginTransaction()
                    If progress IsNot Nothing Then progress("Removing the current records...")
                    For Each t As String In dbTables
                        If Array.IndexOf(KeepOnRestore, t) >= 0 Then Continue For
                        Exec(conn, "DELETE FROM " & Q(t), tx)
                    Next

                    Dim done As Integer = 0
                    Dim lastTable As String = ""
                    ReadStatements(file,
                        Sub(table As String, sql As String)
                            If Array.IndexOf(KeepOnRestore, table) >= 0 Then Return
                            If table <> lastTable Then
                                lastTable = table
                                done += 1
                                If progress IsNot Nothing Then progress("Restoring " & table & " (" & done & " of " & backupTables.Count & ")...")
                            End If
                            If sql.StartsWith("INSERT INTO " & Q(table) & " (") Then Exec(conn, sql, tx)
                        End Sub)

                    If progress IsNot Nothing Then progress("Saving...")
                    tx.Commit()
                End Using
                Exec(conn, "SET UNIQUE_CHECKS = 1")
                Exec(conn, "SET FOREIGN_KEY_CHECKS = 1")

                ' a backup from an older version has no rows for newer tables (e.g. Cash in payment_methods): put them back
                Try
                    SchemaUpgrade.EnsureSeedRows(conn)
                Catch
                    ' ignore: the app still works (Cash is always offered at the POS)
                End Try

                ' the other laptops see the log go back to the start and reload every screen
                Try
                    Exec(conn, "TRUNCATE TABLE change_log")
                Catch
                    ' ignore
                End Try
            End Using
        Catch ex As BackupException
            Throw
        Catch ex As Exception
            Throw New BackupException("The restore was stopped and nothing was changed: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Walks the statements of a backup file, table by table, and hands each allowed one to <paramref name="handle"/>.
    ''' Anything that is not one of the shapes this app writes stops the restore.
    ''' </summary>
    Private Sub ReadStatements(file As String, handle As Action(Of String, String))
        Dim current As String = ""
        Dim lineNo As Integer = 0
        Using sr As New StreamReader(file, New UTF8Encoding(False), False, 1 << 16)
            While True
                Dim line As String = sr.ReadLine()
                If line Is Nothing Then Exit While
                lineNo += 1
                If line.StartsWith(TriggersMarker) Then Exit While
                If line.Length = 0 Then Continue While

                If line.StartsWith(TableMarker) Then
                    Dim m As Match = Regex.Match(line, "^-- @@TABLE `([A-Za-z0-9_]{1,64})`")
                    If Not m.Success Then Throw New BackupException("The backup file is damaged near line " & lineNo & ".")
                    current = m.Groups(1).Value
                    Continue While
                End If
                If line.StartsWith("--") Then Continue While
                If line.StartsWith("SET ") AndAlso line.Length < 80 Then Continue While

                If current.Length = 0 Then Throw New BackupException("The backup file is damaged near line " & lineNo & ".")
                If line.StartsWith("DELETE FROM " & Q(current) & ";") Then Continue While
                If Not line.EndsWith(";") Then Throw New BackupException("The backup file is damaged near line " & lineNo & ".")

                Dim sql As String = line.Substring(0, line.Length - 1)
                If sql.StartsWith("CREATE TABLE IF NOT EXISTS " & Q(current) & " (") OrElse
                   sql.StartsWith("INSERT INTO " & Q(current) & " (") Then
                    handle(current, sql)
                Else
                    Throw New BackupException("The backup file has a statement this app does not run (line " & lineNo & "). It was not restored.")
                End If
            End While
        End Using
    End Sub

    ''' <summary>One statement per command: a changed file can never slip in a second statement.</summary>
    Private Function RestoreConnectionString() As String
        Dim b As New MySqlConnectionStringBuilder(Db.BuildConnectionString(Db.Settings))
        b.AllowBatch = False
        b.DefaultCommandTimeout = 600
        Return b.ConnectionString
    End Function

    ' ================================================================== delete
    ''' <summary>Moves a backup file to the Recycle Bin (only DTSIMS2 backups inside the backup folder).</summary>
    Public Sub DeleteBackup(file As String, folder As String)
        Dim full As String = Path.GetFullPath(file)
        Dim dir As String = Path.GetFullPath(folder).TrimEnd("\"c)
        If Not String.Equals(Path.GetDirectoryName(full), dir, StringComparison.OrdinalIgnoreCase) Then
            Throw New BackupException("Only backups in the backup folder can be deleted here.")
        End If
        If Not ReadInfo(full).IsBackup Then Throw New BackupException("This is not a DTSIMS2 backup file.")
        Try
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(full, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                                                              Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin)
        Catch ex As Exception
            Throw New BackupException("The backup could not be deleted: " & ex.Message)
        End Try
    End Sub

    ' ================================================================== helpers
    Friend Function Q(name As String) As String
        Return "`" & name.Replace("`", "``") & "`"
    End Function

    Friend Sub Exec(conn As MySqlConnection, sql As String, Optional tx As MySqlTransaction = Nothing)
        Using cmd As New MySqlCommand(sql, conn, tx)
            cmd.CommandTimeout = 600
            cmd.ExecuteNonQuery()
        End Using
    End Sub

    Private Function ScalarOn(conn As MySqlConnection, sql As String) As Object
        Using cmd As New MySqlCommand(sql, conn)
            cmd.CommandTimeout = 600
            Return cmd.ExecuteScalar()
        End Using
    End Function

    Private Function OneLine(s As String) As String
        Return Regex.Replace(If(s, ""), "[\r\n\t]+", " ").Trim()
    End Function

    Private Function Left(s As String, n As Integer) As String
        If s Is Nothing OrElse s.Length <= n Then Return s
        Return s.Substring(0, n)
    End Function

    Friend Function ToHex(b As Byte()) As String
        Dim sb As New StringBuilder(b.Length * 2)
        For Each x As Byte In b
            sb.Append(x.ToString("x2", Inv))
        Next
        Return sb.ToString()
    End Function

    Private Sub TryDelete(file As String)
        Try
            If System.IO.File.Exists(file) Then System.IO.File.Delete(file)
        Catch
            ' ignore
        End Try
    End Sub

End Module

''' <summary>Writes UTF-8 lines ending in \n and keeps a SHA-256 of everything written; Finish() adds the checksum line.</summary>
Friend Class HashedLineWriter
    Implements IDisposable

    Private ReadOnly _out As Stream
    Private ReadOnly _sha As SHA256 = SHA256.Create()
    Private ReadOnly _enc As New UTF8Encoding(False)

    Public Sub New(output As Stream)
        _out = output
    End Sub

    Public Sub Line(text As String)
        Dim b As Byte() = _enc.GetBytes(text & ChrW(10))
        _sha.TransformBlock(b, 0, b.Length, Nothing, 0)
        _out.Write(b, 0, b.Length)
    End Sub

    Public Sub Finish()
        _sha.TransformFinalBlock(New Byte() {}, 0, 0)
        Dim b As Byte() = _enc.GetBytes("-- @@CHECKSUM " & BackupService.ToHex(_sha.Hash) & ChrW(10))
        _out.Write(b, 0, b.Length)
        _out.Flush()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        _sha.Dispose()
    End Sub
End Class

''' <summary>One part of the system that a reset can clear.</summary>
Public Class ResetSection
    Public ReadOnly Property Key As String
    Public ReadOnly Property Title As String
    Public ReadOnly Property Description As String
    ''' <summary>Sections that must be cleared too (their records point to this one).</summary>
    Public ReadOnly Property Requires As String()

    Public Sub New(key As String, title As String, description As String, ParamArray requires As String())
        Me.Key = key
        Me.Title = title
        Me.Description = description
        Me.Requires = requires
    End Sub
End Class

''' <summary>
''' Settings > Database Settings > Reset: clears all records or chosen parts, for a fresh start.
''' Always kept: the Superadmin account, VAT, settings, permissions, the backup history.
''' The screen makes a backup first and asks for the Superadmin password and a typed confirmation.
''' </summary>
Public Module SystemReset

    Public ReadOnly Sections As List(Of ResetSection) = Build()

    Private Function Build() As List(Of ResetSection)
        Dim l As New List(Of ResetSection)()
        l.Add(New ResetSection("sales", "Sales and receipts", "Every sale and receipt. Customer point balances stay."))
        l.Add(New ResetSection("deliveries", "Deliveries", "Every supplier delivery and its items."))
        l.Add(New ResetSection("stock", "Stock levels", "Sets every product's stock to 0 and clears the stock movement log."))
        l.Add(New ResetSection("products", "Products and categories", "Every product and category. Also clears sales, deliveries and stock, which use them.",
                               "sales", "deliveries", "stock"))
        l.Add(New ResetSection("suppliers", "Suppliers", "Every supplier. Also clears deliveries, which use them.", "deliveries"))
        l.Add(New ResetSection("customers", "Customers and points", "Every loyalty customer and their points. Past sales keep the customer's name."))
        l.Add(New ResetSection("discounts", "Discounts", "Every discount. Past sales keep the discount's name."))
        l.Add(New ResetSection("users", "Admins and cashiers", "Every account except the Superadmin. Past sales keep the cashier's name."))
        l.Add(New ResetSection("logs", "Activity logs", "The audit trail, duty records (log-in history) and the text message queue."))
        Return l
    End Function

    Public Function Find(key As String) As ResetSection
        For Each s As ResetSection In Sections
            If s.Key = key Then Return s
        Next
        Return Nothing
    End Function

    ''' <summary>The chosen sections plus everything they require.</summary>
    Public Function Expand(chosen As IEnumerable(Of String)) As HashSet(Of String)
        Dim result As New HashSet(Of String)(chosen)
        Dim added As Boolean = True
        While added
            added = False
            For Each k As String In New List(Of String)(result)
                Dim s As ResetSection = Find(k)
                If s Is Nothing Then Continue For
                For Each r As String In s.Requires
                    If result.Add(r) Then added = True
                Next
            Next
        End While
        Return result
    End Function

    ''' <summary>Clears the chosen sections in one transaction (all or nothing).</summary>
    Public Sub Run(chosen As IEnumerable(Of String), progress As Action(Of String))
        Dim keys As HashSet(Of String) = Expand(chosen)
        If keys.Count = 0 Then Throw New BackupException("Choose at least one part to clear.")

        Dim emptied As New List(Of String)()
        Try
            Using conn As MySqlConnection = Db.OpenConnection()
                Using tx As MySqlTransaction = conn.BeginTransaction()
                    ' children before parents, so every foreign key stays satisfied
                    If keys.Contains("sales") Then
                        Say(progress, "Clearing sales and receipts...")
                        Run(conn, tx, "UPDATE points_ledger SET sale_id = NULL WHERE sale_id IS NOT NULL")
                        Run(conn, tx, "DELETE FROM sale_items")
                        Run(conn, tx, "DELETE FROM sales")
                        emptied.AddRange({"sale_items", "sales"})
                    End If
                    If keys.Contains("deliveries") Then
                        Say(progress, "Clearing deliveries...")
                        Run(conn, tx, "DELETE FROM delivery_items")
                        Run(conn, tx, "DELETE FROM deliveries")
                        emptied.AddRange({"delivery_items", "deliveries"})
                    End If
                    If keys.Contains("stock") Then
                        Say(progress, "Clearing stock levels...")
                        Run(conn, tx, "DELETE FROM inventory_movements")
                        Run(conn, tx, "DELETE FROM loss_records")
                        Run(conn, tx, "UPDATE products SET qty_on_hand = 0 WHERE qty_on_hand <> 0")
                        Run(conn, tx, "UPDATE delivery_items SET qty_remaining = 0 WHERE qty_remaining <> 0")
                        emptied.AddRange({"inventory_movements", "loss_records"})
                    End If
                    If keys.Contains("products") Then
                        Say(progress, "Clearing products and categories...")
                        Run(conn, tx, "DELETE FROM products")
                        Run(conn, tx, "DELETE FROM categories")
                        emptied.AddRange({"products", "categories"})
                    End If
                    If keys.Contains("suppliers") Then
                        Say(progress, "Clearing suppliers...")
                        Run(conn, tx, "DELETE FROM suppliers")
                        emptied.Add("suppliers")
                    End If
                    If keys.Contains("customers") Then
                        Say(progress, "Clearing customers and points...")
                        Run(conn, tx, "DELETE FROM points_ledger")
                        Run(conn, tx, "DELETE FROM customers")
                        emptied.AddRange({"points_ledger", "customers"})
                    End If
                    If keys.Contains("discounts") Then
                        Say(progress, "Clearing discounts...")
                        Run(conn, tx, "DELETE FROM discounts")
                        emptied.Add("discounts")
                    End If
                    If keys.Contains("users") Then
                        Say(progress, "Removing admins and cashiers...")
                        Run(conn, tx, "DELETE FROM users WHERE role <> 'superadmin'")
                    End If
                    If keys.Contains("logs") Then
                        Say(progress, "Clearing activity logs...")
                        Run(conn, tx, "DELETE FROM audit_trail")
                        Using cmd As New MySqlCommand("DELETE FROM user_sessions WHERE session_id <> @s", conn, tx)
                            cmd.Parameters.AddWithValue("@s", SessionTracker.CurrentSessionId)
                            cmd.ExecuteNonQuery()
                        End Using
                        Run(conn, tx, "DELETE FROM sms_queue")
                        emptied.AddRange({"audit_trail", "sms_queue"})
                    End If
                    Say(progress, "Saving...")
                    tx.Commit()
                End Using

                ' numbering starts again at 1 in the emptied tables (MySQL keeps it above any row that is left)
                For Each t As String In emptied
                    Try
                        BackupService.Exec(conn, "ALTER TABLE " & BackupService.Q(t) & " AUTO_INCREMENT = 1")
                    Catch
                        ' ignore
                    End Try
                Next
                Try
                    BackupService.Exec(conn, "TRUNCATE TABLE change_log")
                Catch
                    ' ignore
                End Try
            End Using
        Catch ex As BackupException
            Throw
        Catch ex As Exception
            Throw New BackupException("The reset was stopped and nothing was cleared: " & ex.Message)
        End Try
    End Sub

    Private Sub Run(conn As MySqlConnection, tx As MySqlTransaction, sql As String)
        BackupService.Exec(conn, sql, tx)
    End Sub

    Private Sub Say(progress As Action(Of String), text As String)
        If progress IsNot Nothing Then progress(text)
    End Sub

End Module
