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
        Return v
    End Function

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
