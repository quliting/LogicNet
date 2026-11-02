-- =============================================================
-- P0-3 用户注册唯一索引（MySQL，幂等版，可重复执行）
-- 目的：为 UserName / Mobile 建立唯一约束，在数据库层兜底并发注册竞态。
--       应用层已配合捕获 MySQL 错误码 1062 并返回友好提示。
--       全新环境无需执行本脚本：CodeFirst 建表时按实体 SugarIndex 自动创建同名索引。
--
-- 执行前请确认无既有重复数据，否则建索引会失败：
--   SELECT UserName, COUNT(*) c FROM UserInfo GROUP BY UserName HAVING c > 1;
--   SELECT Mobile,  COUNT(*) c FROM UserInfo GROUP BY Mobile  HAVING c > 1;
--
-- 注意：软删除（IsDelete=1）的用户仍占用 UserName/Mobile 唯一值，
--       同名/同号重新注册会被拒绝。如需"删除后可复用"，请评估后调整
--       （例如改为 UserName+IsDelete 联合唯一，或删除时改写原值）。
-- =============================================================

SET @idx_username := (
    SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE() AND table_name = 'UserInfo' AND index_name = 'UX_UserInfo_UserName');
SET @sql_username := IF(@idx_username = 0,
    'ALTER TABLE `UserInfo` ADD UNIQUE INDEX `UX_UserInfo_UserName` (`UserName`)',
    'SELECT ''UX_UserInfo_UserName 已存在，跳过''');
PREPARE stmt_username FROM @sql_username;
EXECUTE stmt_username;
DEALLOCATE PREPARE stmt_username;

SET @idx_mobile := (
    SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE() AND table_name = 'UserInfo' AND index_name = 'UX_UserInfo_Mobile');
SET @sql_mobile := IF(@idx_mobile = 0,
    'ALTER TABLE `UserInfo` ADD UNIQUE INDEX `UX_UserInfo_Mobile` (`Mobile`)',
    'SELECT ''UX_UserInfo_Mobile 已存在，跳过''');
PREPARE stmt_mobile FROM @sql_mobile;
EXECUTE stmt_mobile;
DEALLOCATE PREPARE stmt_mobile;
