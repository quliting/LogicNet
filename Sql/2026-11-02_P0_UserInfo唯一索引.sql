-- =============================================================
-- P0-3 用户注册唯一索引（MySQL）
-- 目的：为 UserName / Mobile 建立唯一约束，在数据库层兜底并发注册竞态。
--       应用层已配合捕获 MySQL 错误码 1062 并返回友好提示。
--
-- 执行前请确认无既有重复数据，否则建索引会失败：
--   SELECT UserName, COUNT(*) c FROM UserInfo GROUP BY UserName HAVING c > 1;
--   SELECT Mobile,  COUNT(*) c FROM UserInfo GROUP BY Mobile  HAVING c > 1;
--
-- 注意：软删除（IsDelete=1）的用户仍占用 UserName/Mobile 唯一值，
--       同名/同号重新注册会被拒绝。如需"删除后可复用"，请评估后调整
--       （例如改为 UserName+IsDelete 联合唯一，或删除时改写原值）。
-- =============================================================

ALTER TABLE `UserInfo` ADD UNIQUE INDEX `UX_UserInfo_UserName` (`UserName`);
ALTER TABLE `UserInfo` ADD UNIQUE INDEX `UX_UserInfo_Mobile` (`Mobile`);
