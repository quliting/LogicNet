using System;
using SqlSugar;

namespace LogicNet.Core.Entity;

public class UserInfo : BaseEntity
{
    public string Email { get; set; }=String.Empty;
    public string UserName { get; set; }=String.Empty;
    public string Name { get; set; }
    public string Mobile { get; set; }
}