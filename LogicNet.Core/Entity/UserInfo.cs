using SqlSugar;

namespace LogicNet.Core.Entity;

public class UserInfo : BaseEntity
{
    public string Email { get; set; }
    public string UserName { get; set; }
    public string Name { get; set; }
    public string Mobile { get; set; }
}