using System;
using SqlSugar;

namespace LogicNet.Core.Entity;

public abstract class BaseEntity
{
    public bool IsDelete { get; set; } = false;

    [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }

    [SugarColumn(UpdateServerTime = true)] public DateTime UpdateTime { get; set; } = DateTime.Now;
    [SugarColumn(InsertServerTime = true)] public DateTime CreateTime { get; set; } = DateTime.Now;

    public long CreateUserId { get; set; }
    public long UpdateUserId { get; set; }
}