-- Schema version 6: SMS queue.
-- The app needs version 6. If you set up a fresh database from dtsims2_db.sql (which is version 5), run this once on it.
-- Safe to run more than once. Select the database first (USE dtsims2_db;) or pick it in MySQL Workbench.

CREATE TABLE IF NOT EXISTS `sms_queue` (
  `id` int NOT NULL AUTO_INCREMENT,
  `phone_number` varchar(30) NOT NULL,
  `message` varchar(480) NOT NULL,
  `status` enum('Pending','Sending','Failed') NOT NULL DEFAULT 'Pending',
  `attempts` int NOT NULL DEFAULT '0',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  KEY `idx_sms_status` (`status`,`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT IGNORE INTO `schema_version` (`version`, `description`) VALUES (6, 'SMS queue');

SELECT MAX(version) AS schema_version_now FROM schema_version;   -- should say 6
