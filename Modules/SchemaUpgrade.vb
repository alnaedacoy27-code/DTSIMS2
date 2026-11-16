Option Strict On
Option Explicit On

Imports MySql.Data.MySqlClient

''' <summary>
''' Brings an older database up to the version this app needs, by itself, at startup.
''' Every step is safe to run more than once. The same SQL is in Database/07_permissions_and_reset.sql.
''' </summary>
Public Module SchemaUpgrade

    ''' <summary>Runs the upgrades from <paramref name="fromVersion"/>. Returns the version the database is now at.</summary>
    Public Function Apply(conn As MySqlConnection, fromVersion As Integer) As Integer
        Dim v As Integer = fromVersion
        If v = 6 Then
            UpgradeTo7(conn)
            v = 7
        End If
        If v = 7 Then
            UpgradeTo8(conn)
            v = 8
        End If
        Return v
    End Function

    ' 8: one computer per account (why a session ended), payment methods, and how each sale was paid
    Private Sub UpgradeTo8(conn As MySqlConnection)
        AddColumnIfMissing(conn, "user_sessions", "end_reason", "varchar(100) DEFAULT NULL")

        Exec(conn,
            "CREATE TABLE IF NOT EXISTS `payment_methods` (" &
            "`id` int NOT NULL AUTO_INCREMENT, " &
            "`name` varchar(60) NOT NULL, " &
            "`kind` enum('cash','ewallet','bank','card','other') NOT NULL DEFAULT 'ewallet', " &
            "`account_name` varchar(100) DEFAULT NULL, " &
            "`account_number` varchar(60) DEFAULT NULL, " &
            "`qr_image` mediumblob, " &
            "`needs_reference` tinyint(1) NOT NULL DEFAULT '1', " &
            "`is_active` tinyint(1) NOT NULL DEFAULT '1', " &
            "`sort_order` int NOT NULL DEFAULT '0', " &
            "`created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP, " &
            "`updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP, " &
            "PRIMARY KEY (`id`), UNIQUE KEY `uq_payment_name` (`name`)" &
            ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci")
        Exec(conn, "INSERT IGNORE INTO payment_methods (name, kind, needs_reference, is_active, sort_order) VALUES ('Cash', 'cash', 0, 1, 0)")

        AddColumnIfMissing(conn, "sales", "payment_method_id", "int DEFAULT NULL")
        AddColumnIfMissing(conn, "sales", "payment_method", "varchar(60) DEFAULT NULL")
        AddColumnIfMissing(conn, "sales", "payment_reference", "varchar(60) DEFAULT NULL")

        SeedRoleRows(conn)
        Exec(conn, "INSERT IGNORE INTO schema_version (version, description) VALUES (8, 'One computer per account, payment methods')")
    End Sub

    ''' <summary>
    ''' Rows the app always needs, put back after restoring a backup made by an older version:
    ''' the Cash payment method and a role row for every module.
    ''' </summary>
    Public Sub EnsureSeedRows(conn As MySqlConnection)
        Exec(conn, "INSERT IGNORE INTO payment_methods (name, kind, needs_reference, is_active, sort_order) VALUES ('Cash', 'cash', 0, 1, 0)")
        SeedRoleRows(conn)
    End Sub

    ''' <summary>MySQL has no ADD COLUMN IF NOT EXISTS: look first, so the upgrade can run twice.</summary>
    Private Sub AddColumnIfMissing(conn As MySqlConnection, table As String, column As String, definition As String)
        Using cmd As New MySqlCommand("SELECT COUNT(*) FROM information_schema.columns " &
                                      "WHERE table_schema = DATABASE() AND table_name = @t AND column_name = @c", conn)
            cmd.Parameters.AddWithValue("@t", table)
            cmd.Parameters.AddWithValue("@c", column)
            If Convert.ToInt32(cmd.ExecuteScalar()) > 0 Then Return
        End Using
        Exec(conn, "ALTER TABLE `" & table & "` ADD COLUMN `" & column & "` " & definition)
    End Sub

    ''' <summary>A role_permissions row for every module and editable role, with the factory setting (rows that exist are kept).</summary>
    Private Sub SeedRoleRows(conn As MySqlConnection)
        For Each r As String In Permissions.EditableRoles
            For Each m As ModuleInfo In ModuleCatalog.All
                If Permissions.IsSuperadminOnly(m.Key) Then Continue For
                Using cmd As New MySqlCommand(
                    "INSERT IGNORE INTO role_permissions (role, module_key, is_allowed) VALUES (@r, @k, @a)", conn)
                    cmd.Parameters.AddWithValue("@r", r)
                    cmd.Parameters.AddWithValue("@k", m.Key)
                    cmd.Parameters.AddWithValue("@a", If(Permissions.DefaultAccess(r, m.Key), 1, 0))
                    cmd.ExecuteNonQuery()
                End Using
            Next
        Next
    End Sub

    ' 7: per-user permissions, a "before reset" backup type, and role rows for every module
    Private Sub UpgradeTo7(conn As MySqlConnection)
        Exec(conn,
            "CREATE TABLE IF NOT EXISTS `user_permissions` (" &
            "`user_id` int NOT NULL, " &
            "`module_key` varchar(40) NOT NULL, " &
            "`is_allowed` tinyint(1) NOT NULL, " &
            "`updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP, " &
            "PRIMARY KEY (`user_id`, `module_key`), " &
            "CONSTRAINT `fk_userperm_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE" &
            ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci")

        Exec(conn,
            "ALTER TABLE `backup_log` MODIFY `backup_type` " &
            "enum('manual','auto','pre_restore','pre_reset') NOT NULL DEFAULT 'manual'")

        ' a row for every module and editable role, with the factory setting (rows that exist are kept)
        For Each r As String In Permissions.EditableRoles
            For Each m As ModuleInfo In ModuleCatalog.All
                If Permissions.IsSuperadminOnly(m.Key) Then Continue For
                Using cmd As New MySqlCommand(
                    "INSERT IGNORE INTO role_permissions (role, module_key, is_allowed) VALUES (@r, @k, @a)", conn)
                    cmd.Parameters.AddWithValue("@r", r)
                    cmd.Parameters.AddWithValue("@k", m.Key)
                    cmd.Parameters.AddWithValue("@a", If(Permissions.DefaultAccess(r, m.Key), 1, 0))
                    cmd.ExecuteNonQuery()
                End Using
            Next
        Next

        Exec(conn, "INSERT IGNORE INTO schema_version (version, description) VALUES (7, 'Permission maintenance, backup and system reset')")
    End Sub

    Private Sub Exec(conn As MySqlConnection, sql As String)
        Using cmd As New MySqlCommand(sql, conn)
            cmd.ExecuteNonQuery()
        End Using
    End Sub

End Module
