-- Schema version 7: per-user permissions (Settings > Permission Maintenance) and the "before reset" backup type.
-- The app makes these changes by itself the first time it starts on a version 6 database.
-- Run this by hand only if the app says it could not update the database.
-- Safe to run more than once. Select the database first (USE dtsims2_db;) or pick it in MySQL Workbench.

CREATE TABLE IF NOT EXISTS `user_permissions` (
  `user_id` int NOT NULL,
  `module_key` varchar(40) NOT NULL,
  `is_allowed` tinyint(1) NOT NULL,
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`user_id`, `module_key`),
  CONSTRAINT `fk_userperm_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

ALTER TABLE `backup_log` MODIFY `backup_type` enum('manual','auto','pre_restore','pre_reset') NOT NULL DEFAULT 'manual';

-- role rows the app adds for modules that had none (factory settings)
INSERT IGNORE INTO `role_permissions` (`role`, `module_key`, `is_allowed`) VALUES
  ('admin', 'delivery_report', 1), ('admin', 'devices', 1), ('admin', 'text_size', 1),
  ('cashier', 'delivery_report', 0), ('cashier', 'devices', 0), ('cashier', 'text_size', 1);

INSERT IGNORE INTO `schema_version` (`version`, `description`) VALUES (7, 'Permission maintenance, backup and system reset');

SELECT MAX(version) AS schema_version_now FROM schema_version;   -- should say 7
