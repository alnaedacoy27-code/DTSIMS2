-- Schema version 9: each payment method's reference number rule.
-- The app makes this change by itself the first time it starts on a version 8 database.
-- Run this by hand only if the app says it could not update the database (run 07 and 08 first if needed).
-- The ALTER TABLE lines fail with "Duplicate column name" if they were already applied: that is fine, go on.

ALTER TABLE `payment_methods` ADD COLUMN `ref_digits_only` tinyint(1) NOT NULL DEFAULT '0';
ALTER TABLE `payment_methods` ADD COLUMN `ref_min_len` int NOT NULL DEFAULT '4';
ALTER TABLE `payment_methods` ADD COLUMN `ref_max_len` int NOT NULL DEFAULT '40';

INSERT IGNORE INTO `schema_version` (`version`, `description`) VALUES (9, 'Reference number rule per payment method');

SELECT MAX(version) AS schema_version_now FROM schema_version;   -- should say 9
