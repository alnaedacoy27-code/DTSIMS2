-- Schema version 8: one computer per account, payment methods, and how each sale was paid.
-- The app makes these changes by itself the first time it starts on a version 7 database.
-- Run this by hand only if the app says it could not update the database (run 07 first if the database is at 6).
-- Select the database first (USE dtsims2_db;) or pick it in MySQL Workbench.
-- The ALTER TABLE lines fail with "Duplicate column name" if they were already applied: that is fine, go on.

ALTER TABLE `user_sessions` ADD COLUMN `end_reason` varchar(100) DEFAULT NULL;

CREATE TABLE IF NOT EXISTS `payment_methods` (
  `id` int NOT NULL AUTO_INCREMENT,
  `name` varchar(60) NOT NULL,
  `kind` enum('cash','ewallet','bank','card','other') NOT NULL DEFAULT 'ewallet',
  `account_name` varchar(100) DEFAULT NULL,
  `account_number` varchar(60) DEFAULT NULL,
  `qr_image` mediumblob,
  `needs_reference` tinyint(1) NOT NULL DEFAULT '1',
  `is_active` tinyint(1) NOT NULL DEFAULT '1',
  `sort_order` int NOT NULL DEFAULT '0',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_payment_name` (`name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT IGNORE INTO `payment_methods` (`name`, `kind`, `needs_reference`, `is_active`, `sort_order`) VALUES ('Cash', 'cash', 0, 1, 0);

ALTER TABLE `sales` ADD COLUMN `payment_method_id` int DEFAULT NULL;
ALTER TABLE `sales` ADD COLUMN `payment_method` varchar(60) DEFAULT NULL;
ALTER TABLE `sales` ADD COLUMN `payment_reference` varchar(60) DEFAULT NULL;

INSERT IGNORE INTO `role_permissions` (`role`, `module_key`, `is_allowed`) VALUES
  ('admin', 'payment_methods', 0), ('cashier', 'payment_methods', 0);

INSERT IGNORE INTO `schema_version` (`version`, `description`) VALUES (8, 'One computer per account, payment methods');

SELECT MAX(version) AS schema_version_now FROM schema_version;   -- should say 8
