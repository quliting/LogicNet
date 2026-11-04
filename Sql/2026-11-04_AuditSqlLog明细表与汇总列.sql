-- =============================================================
-- 操作审计日志：SQL 执行明细增量脚本（MySQL 8，幂等版，可重复执行）
-- 目的：为存量库补齐 AuditLog 汇总列，并创建 AuditSqlLog 明细表与索引。
--
-- 前置：AuditLog 表已存在。全新环境由 DatabaseInitializer 的 CodeFirst 自动建表；
--       存量库若尚未执行过审计建表脚本，请先执行：
--       Sql/2026-11-04_AuditLog表与索引.sql
-- 说明：本脚本所有改动均先查 information_schema，已存在时自动跳过，可安全重复执行。
-- =============================================================

SET @has_auditlog := (
    SELECT COUNT(*) FROM information_schema.tables
    WHERE table_schema = DATABASE() AND table_name = 'AuditLog');

-- ---------- AuditLog 汇总列 ----------
SET @col_sqlcount := (
    SELECT COUNT(*) FROM information_schema.columns
    WHERE table_schema = DATABASE() AND table_name = 'AuditLog' AND column_name = 'SqlCount');
SET @sql_sqlcount := IF(@has_auditlog = 1 AND @col_sqlcount = 0,
    'ALTER TABLE `AuditLog` ADD COLUMN `SqlCount` INT NOT NULL DEFAULT 0',
    'SELECT ''SqlCount 已存在或 AuditLog 不存在，跳过''');
PREPARE stmt_sqlcount FROM @sql_sqlcount;
EXECUTE stmt_sqlcount;
DEALLOCATE PREPARE stmt_sqlcount;

SET @col_sqlelapsed := (
    SELECT COUNT(*) FROM information_schema.columns
    WHERE table_schema = DATABASE() AND table_name = 'AuditLog' AND column_name = 'SqlElapsedMs');
SET @sql_sqlelapsed := IF(@has_auditlog = 1 AND @col_sqlelapsed = 0,
    'ALTER TABLE `AuditLog` ADD COLUMN `SqlElapsedMs` BIGINT NOT NULL DEFAULT 0',
    'SELECT ''SqlElapsedMs 已存在或 AuditLog 不存在，跳过''');
PREPARE stmt_sqlelapsed FROM @sql_sqlelapsed;
EXECUTE stmt_sqlelapsed;
DEALLOCATE PREPARE stmt_sqlelapsed;

SET @col_slowsqlcount := (
    SELECT COUNT(*) FROM information_schema.columns
    WHERE table_schema = DATABASE() AND table_name = 'AuditLog' AND column_name = 'SlowSqlCount');
SET @sql_slowsqlcount := IF(@has_auditlog = 1 AND @col_slowsqlcount = 0,
    'ALTER TABLE `AuditLog` ADD COLUMN `SlowSqlCount` INT NOT NULL DEFAULT 0',
    'SELECT ''SlowSqlCount 已存在或 AuditLog 不存在，跳过''');
PREPARE stmt_slowsqlcount FROM @sql_slowsqlcount;
EXECUTE stmt_slowsqlcount;
DEALLOCATE PREPARE stmt_slowsqlcount;

-- ---------- AuditSqlLog 明细表 ----------
CREATE TABLE IF NOT EXISTS `AuditSqlLog` (
    `Id` BIGINT NOT NULL,
    `AuditLogId` BIGINT NOT NULL,
    `TraceId` VARCHAR(128) NULL,
    `SqlText` VARCHAR(2000) NOT NULL,
    `Parameters` VARCHAR(2000) NULL,
    `ElapsedMs` BIGINT NOT NULL DEFAULT 0,
    `IsSlow` TINYINT(1) NOT NULL DEFAULT 0,
    `ErrorMessage` VARCHAR(1000) NULL,
    `IsDelete` TINYINT(1) NOT NULL DEFAULT 0,
    `CreateTime` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `UpdateTime` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `CreateUserId` BIGINT NOT NULL DEFAULT 0,
    `UpdateUserId` BIGINT NOT NULL DEFAULT 0,
    PRIMARY KEY (`Id`)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

-- 兼容早先建过 AuditSqlLog 但缺少 ErrorMessage 的库（新增列同样幂等）
SET @col_errormessage := (
    SELECT COUNT(*) FROM information_schema.columns
    WHERE table_schema = DATABASE() AND table_name = 'AuditSqlLog' AND column_name = 'ErrorMessage');
SET @sql_errormessage := IF(@col_errormessage = 0,
    'ALTER TABLE `AuditSqlLog` ADD COLUMN `ErrorMessage` VARCHAR(1000) NULL',
    'SELECT ''ErrorMessage 已存在，跳过''');
PREPARE stmt_errormessage FROM @sql_errormessage;
EXECUTE stmt_errormessage;
DEALLOCATE PREPARE stmt_errormessage;

-- IX_AuditSqlLog_AuditLogId：详情按所属审计记录取明细、清理按主记录 Id 删除
SET @idx_auditlogid := (
    SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE() AND table_name = 'AuditSqlLog' AND index_name = 'IX_AuditSqlLog_AuditLogId');
SET @sql_idx_auditlogid := IF(@idx_auditlogid = 0,
    'ALTER TABLE `AuditSqlLog` ADD INDEX `IX_AuditSqlLog_AuditLogId` (`AuditLogId`)',
    'SELECT ''IX_AuditSqlLog_AuditLogId 已存在，跳过''');
PREPARE stmt_idx_auditlogid FROM @sql_idx_auditlogid;
EXECUTE stmt_idx_auditlogid;
DEALLOCATE PREPARE stmt_idx_auditlogid;

-- IX_AuditSqlLog_TraceId：按链路 Id 关联排查
SET @idx_traceid := (
    SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE() AND table_name = 'AuditSqlLog' AND index_name = 'IX_AuditSqlLog_TraceId');
SET @sql_idx_traceid := IF(@idx_traceid = 0,
    'ALTER TABLE `AuditSqlLog` ADD INDEX `IX_AuditSqlLog_TraceId` (`TraceId`)',
    'SELECT ''IX_AuditSqlLog_TraceId 已存在，跳过''');
PREPARE stmt_idx_traceid FROM @sql_idx_traceid;
EXECUTE stmt_idx_traceid;
DEALLOCATE PREPARE stmt_idx_traceid;

-- IX_AuditSqlLog_CreateTime：按时间区间查询与过期清理
SET @idx_createtime := (
    SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE() AND table_name = 'AuditSqlLog' AND index_name = 'IX_AuditSqlLog_CreateTime');
SET @sql_idx_createtime := IF(@idx_createtime = 0,
    'ALTER TABLE `AuditSqlLog` ADD INDEX `IX_AuditSqlLog_CreateTime` (`CreateTime` DESC)',
    'SELECT ''IX_AuditSqlLog_CreateTime 已存在，跳过''');
PREPARE stmt_idx_createtime FROM @sql_idx_createtime;
EXECUTE stmt_idx_createtime;
DEALLOCATE PREPARE stmt_idx_createtime;
