using System;
using SqlSugar;

namespace LogicNet.Core.Entity;

// 唯一索引标在类上（SugarIndex 仅支持类声明）；CodeFirst 建新表时创建，
// 存量库执行 Sql/2026-11-02_P0_UserInfo唯一索引.sql 幂等脚本
[SugarIndex("UX_UserInfo_UserName", nameof(UserName), OrderByType.Asc, true)]
[SugarIndex("UX_UserInfo_Mobile", nameof(Mobile), OrderByType.Asc, true)]
public class UserInfo : BaseEntity
{
    [SugarColumn(IsNullable = true)]
    public string Email { get; set; }=String.Empty;
    public string UserName { get; set; }=String.Empty;
    public string Name { get; set; }
    public string Mobile { get; set; }
    public string Password { get; set; }
}