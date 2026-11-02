namespace LogicNet.Core.Entity;

/// <summary>
///     用户-角色关联
/// </summary>
public class UserRole : BaseEntity
{
    /// <summary>
    ///     用户 Id
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    ///     角色 Id
    /// </summary>
    public long RoleId { get; set; }
}
