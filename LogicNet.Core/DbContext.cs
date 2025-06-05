using Furion;
using SqlSugar;
using System.Collections.Generic;

namespace LogicNet.Core;

/// <summary>
/// 数据库上下文对象
/// </summary>
public static class DbContext
{
    
}

public class Repository<T> : SimpleClient<T> where T : class, new()
{
    public Repository(ISqlSugarClient db)
    {
        base.Context = db;
    }
    
    
}