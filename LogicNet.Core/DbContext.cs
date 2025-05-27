using Furion;
using SqlSugar;
using System.Collections.Generic;

namespace LogicNet.Core;

/// <summary>
/// 数据库上下文对象
/// </summary>
public static class DbContext
{
    /// <summary>
    /// SqlSugar 数据库实例
    /// </summary>
    // public static readonly SqlSugarScope Instance = new(
    //     // 读取 appsettings.json 中的 ConnectionConfigs 配置节点
    //     App.GetConfig<List<ConnectionConfig>>("ConnectionConfigs")
    //     , db =>
    //     {
    //         db.CodeFirst.InitTables();
    //
    //         var sqlsugar = new SqlSugarClient(db.CurrentConnectionConfig);
    //         // 这里配置全局事件，比如拦截执行 SQL
    //          
    //     });
}

public class Repository<T> : SimpleClient<T> where T : class, new()
{
    public Repository(ISqlSugarClient context = null) : base(context)
    {
        base.Context = App.GetService<ISqlSugarClient>();
    }
}