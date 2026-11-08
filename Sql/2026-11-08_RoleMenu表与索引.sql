-- =============================================================
-- 角色-菜单关联（MySQL，幂等版，可重复执行）
-- 目的：CodeFirst 建表未生效时补齐 RoleMenu 表与索引，并灌入 Admin 全量关联。
--       全新环境由 DatabaseInitializer 自动建表并创建同名索引，本脚本重复执行为空操作。
--
-- 注意软删除：IsDelete=1 的关联仍占用 (RoleId, MenuId) 唯一索引，
--       重新勾选由应用层“恢复”软删除行处理；本脚本仅补插从未存在的关联。
-- =============================================================

CREATE TABLE IF NOT EXISTS `RoleMenu` (
    `Id` BIGINT NOT NULL,
    `RoleId` BIGINT NOT NULL,
    `MenuId` BIGINT NOT NULL,
    `IsDelete` TINYINT(1) NOT NULL DEFAULT 0,
    `UpdateTime` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `CreateTime` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `CreateUserId` BIGINT NOT NULL DEFAULT 0,
    `UpdateUserId` BIGINT NOT NULL DEFAULT 0,
    PRIMARY KEY (`Id`)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

-- IX_RoleMenu_MenuId：按菜单反查角色授权
SET @idx_menu := (
    SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE() AND table_name = 'RoleMenu' AND index_name = 'IX_RoleMenu_MenuId');
SET @sql_menu := IF(@idx_menu = 0,
    'ALTER TABLE `RoleMenu` ADD INDEX `IX_RoleMenu_MenuId` (`MenuId`)',
    'SELECT ''IX_RoleMenu_MenuId 已存在，跳过''');
PREPARE stmt_menu FROM @sql_menu;
EXECUTE stmt_menu;
DEALLOCATE PREPARE stmt_menu;

-- UX_RoleMenu_Role_Menu：同一角色对同一菜单只保留一条（并发写入兜底）
SET @idx_unique := (
    SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE() AND table_name = 'RoleMenu' AND index_name = 'UX_RoleMenu_Role_Menu');
SET @sql_unique := IF(@idx_unique = 0,
    'ALTER TABLE `RoleMenu` ADD UNIQUE INDEX `UX_RoleMenu_Role_Menu` (`RoleId`, `MenuId`)',
    'SELECT ''UX_RoleMenu_Role_Menu 已存在，跳过''');
PREPARE stmt_unique FROM @sql_unique;
EXECUTE stmt_unique;
DEALLOCATE PREPARE stmt_unique;

-- 种子：Admin 角色关联全部未删除菜单（含软删除关联判断，保证可重复执行）
-- Id 用 UUID_SHORT 生成，仅用于本脚本兜底；应用侧仍由雪花 Id 统一生成。
INSERT INTO `RoleMenu` (`Id`, `RoleId`, `MenuId`, `IsDelete`, `UpdateTime`, `CreateTime`,
    `CreateUserId`, `UpdateUserId`)
SELECT UUID_SHORT(), r.`Id`, m.`Id`, 0, NOW(), NOW(), 0, 0
FROM `Role` r
JOIN `Menu` m ON m.`IsDelete` = 0
WHERE r.`Name` = 'Admin'
  AND r.`IsDelete` = 0
  AND NOT EXISTS (
      SELECT 1 FROM `RoleMenu` rm
      WHERE rm.`RoleId` = r.`Id` AND rm.`MenuId` = m.`Id`);
