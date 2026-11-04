-- =============================================================
-- 操作审计日志表（MySQL，幂等版，可重复执行）
-- 目的：CodeFirst 建表未生效时补齐 AuditLog 表与索引，不依赖应用启动。
--       全新环境由 DatabaseInitializer 自动建表并创建同名索引，本脚本为空操作。
--
-- 时间字段约定：CreateTime 即对外 DTO 的 CreatedAt（复用 BaseEntity 字段）。
-- 数据保留：默认保留 90 天，由每日清理任务/管理端清理接口按 CreateTime 物理删除。
-- =============================================================

CREATE TABLE IF NOT EXISTS `AuditLog` (
    `Id` BIGINT NOT NULL,
    `UserId` BIGINT NULL,
    `UserName` VARCHAR(100) NULL,
    `HttpMethod` VARCHAR(10) NOT NULL,
    `Path` VARCHAR(500) NOT NULL,
    `QueryString` VARCHAR(1024) NULL,
    `RequestBody` VARCHAR(2048) NULL,
    `StatusCode` INT NOT NULL,
    `ResultCode` INT NULL,
    `Succeeded` TINYINT(1) NOT NULL DEFAULT 0,
    `ClientIp` VARCHAR(64) NULL,
    `UserAgent` VARCHAR(500) NULL,
    `TraceId` VARCHAR(128) NULL,
    `DurationMs` BIGINT NOT NULL DEFAULT 0,
    `ExceptionMessage` VARCHAR(1024) NULL,
    `IsDelete` TINYINT(1) NOT NULL DEFAULT 0,
    `CreateTime` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `UpdateTime` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `CreateUserId` BIGINT NOT NULL DEFAULT 0,
    `UpdateUserId` BIGINT NOT NULL DEFAULT 0,
    PRIMARY KEY (`Id`)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

-- IX_AuditLog_CreateTime：按时间区间查询与过期清理
SET @idx_create := (
    SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE() AND table_name = 'AuditLog' AND index_name = 'IX_AuditLog_CreateTime');
SET @sql_create := IF(@idx_create = 0,
    'ALTER TABLE `AuditLog` ADD INDEX `IX_AuditLog_CreateTime` (`CreateTime` DESC)',
    'SELECT ''IX_AuditLog_CreateTime 已存在，跳过''');
PREPARE stmt_create FROM @sql_create;
EXECUTE stmt_create;
DEALLOCATE PREPARE stmt_create;

-- IX_AuditLog_UserId：按用户筛选
SET @idx_user := (
    SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE() AND table_name = 'AuditLog' AND index_name = 'IX_AuditLog_UserId');
SET @sql_user := IF(@idx_user = 0,
    'ALTER TABLE `AuditLog` ADD INDEX `IX_AuditLog_UserId` (`UserId`)',
    'SELECT ''IX_AuditLog_UserId 已存在，跳过''');
PREPARE stmt_user FROM @sql_user;
EXECUTE stmt_user;
DEALLOCATE PREPARE stmt_user;

-- IX_AuditLog_Path：按路径关键字筛选
SET @idx_path := (
    SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE() AND table_name = 'AuditLog' AND index_name = 'IX_AuditLog_Path');
SET @sql_path := IF(@idx_path = 0,
    'ALTER TABLE `AuditLog` ADD INDEX `IX_AuditLog_Path` (`Path`)',
    'SELECT ''IX_AuditLog_Path 已存在，跳过''');
PREPARE stmt_path FROM @sql_path;
EXECUTE stmt_path;
DEALLOCATE PREPARE stmt_path;
