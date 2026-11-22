-- Suppliers: different companies may share an address; only the same company at the same address is a duplicate.
-- The app makes this change by itself when it starts (no new database version).
-- Run this by hand only if the app could not (for example the MySQL account has no ALTER right).
-- "Duplicate key name" on the first line or "Can't DROP" on the second means it was already done: that is fine.

ALTER TABLE `suppliers` ADD UNIQUE KEY `uq_suppliers_company_address` (`company_name`, `company_address`);
ALTER TABLE `suppliers` DROP INDEX `uq_suppliers_address`;

SHOW INDEX FROM `suppliers`;   -- should list uq_suppliers_company_address and no uq_suppliers_address
