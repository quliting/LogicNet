-- =============================================================
-- 菜单维护（MySQL，幂等版，可重复执行）
-- 目的：CodeFirst 建表未生效时补齐 Menu 表与索引，不依赖应用启动。
--       全新环境由 DatabaseInitializer 自动建表并创建同名索引，本脚本为空操作。
--
-- 注意软删除：IsDelete=1 的菜单仍占用 (ParentId, Path) 唯一索引，
--       删除后同路径重建会被数据库拒绝；需要复用路径请先物理清理旧行。
-- =============================================================

CREATE TABLE IF NOT EXISTS `Menu` (
    `Id` BIGINT NOT NULL,
    `ParentId` BIGINT NOT NULL DEFAULT 0,
    `Name` VARCHAR(100) NOT NULL,
    `Path` VARCHAR(255) NOT NULL,
    `Component` VARCHAR(255) NULL,
    `Icon` VARCHAR(100) NULL,
    `Sort` INT NOT NULL DEFAULT 0,
    `Type` INT NOT NULL DEFAULT 2,
    `PermissionCode` VARCHAR(100) NULL,
    `IsEnabled` TINYINT(1) NOT NULL DEFAULT 1,
    `IsVisible` TINYINT(1) NOT NULL DEFAULT 1,
    `IsDelete` TINYINT(1) NOT NULL DEFAULT 0,
    `UpdateTime` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `CreateTime` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `CreateUserId` BIGINT NOT NULL DEFAULT 0,
    `UpdateUserId` BIGINT NOT NULL DEFAULT 0,
    PRIMARY KEY (`Id`)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

-- IX_Menu_ParentId
SET @idx_parent := (
    SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE() AND table_name = 'Menu' AND index_name = 'IX_Menu_ParentId');
SET @sql_parent := IF(@idx_parent = 0,
    'ALTER TABLE `Menu` ADD INDEX `IX_Menu_ParentId` (`ParentId`)',
    'SELECT ''IX_Menu_ParentId 已存在，跳过''');
PREPARE stmt_parent FROM @sql_parent;
EXECUTE stmt_parent;
DEALLOCATE PREPARE stmt_parent;

-- IX_Menu_Sort
SET @idx_sort := (
    SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE() AND table_name = 'Menu' AND index_name = 'IX_Menu_Sort');
SET @sql_sort := IF(@idx_sort = 0,
    'ALTER TABLE `Menu` ADD INDEX `IX_Menu_Sort` (`Sort`)',
    'SELECT ''IX_Menu_Sort 已存在，跳过''');
PREPARE stmt_sort FROM @sql_sort;
EXECUTE stmt_sort;
DEALLOCATE PREPARE stmt_sort;

-- UX_Menu_Parent_Path：同一上级下路由唯一（并发写入兜底）
SET @idx_unique := (
    SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE() AND table_name = 'Menu' AND index_name = 'UX_Menu_Parent_Path');
SET @sql_unique := IF(@idx_unique = 0,
    'ALTER TABLE `Menu` ADD UNIQUE INDEX `UX_Menu_Parent_Path` (`ParentId`, `Path`)',
    'SELECT ''UX_Menu_Parent_Path 已存在，跳过''');
PREPARE stmt_unique FROM @sql_unique;
EXECUTE stmt_unique;
DEALLOCATE PREPARE stmt_unique;
