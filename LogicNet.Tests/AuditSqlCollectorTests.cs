using System;
using LogicNet.Web.Core.Audit;
using Microsoft.AspNetCore.Http;
using SqlSugar;
using Xunit;

namespace LogicNet.Tests;

/// <summary>
///     SQL 明细采集：默认只记慢 SQL 与失败 SQL、全量开关、慢判定、脱敏、截断、条数上限与请求隔离
/// </summary>
public class AuditSqlCollectorTests
{
    private static SugarParameter[] Params(params (string Name, object Value)[] values)
    {
        return values.Select(item => new SugarParameter(item.Name, item.Value)).ToArray();
    }

    [Fact]
    public void 打开全量开关后记录全部SQL并累计耗时与慢SQL()
    {
        var collector = new AuditSqlCollector(new AuditSqlOptions { RecordAll = true });

        collector.Add("INSERT INTO Menu(Name) VALUES(@name)", Params(("@name", "菜单")), 3);
        collector.Add("SELECT * FROM Menu", null, 600);

        var snapshot = collector.Close();

        Assert.Equal(2, snapshot.Count);
        Assert.Equal(603, snapshot.ElapsedMs);
        Assert.Equal(1, snapshot.SlowCount);
        Assert.Equal(2, snapshot.Items.Count);
        Assert.True(snapshot.Items[1].IsSlow);
    }

    [Fact]
    public void 默认只记录慢SQL_正常快SQL只计数不落明细()
    {
        var collector = new AuditSqlCollector(new AuditSqlOptions { SlowThresholdMs = 200 });

        collector.Add("SELECT 1", null, 5);
        collector.Add("SELECT 2", null, 200);

        var snapshot = collector.Close();

        Assert.Equal(2, snapshot.Count);
        Assert.Equal(205, snapshot.ElapsedMs);
        Assert.Equal(1, snapshot.SlowCount);
        Assert.Single(snapshot.Items);
        Assert.Equal(200, snapshot.Items[0].ElapsedMs);
    }

    [Fact]
    public void 默认阈值为500毫秒_300毫秒不记为慢SQL()
    {
        var collector = new AuditSqlCollector(new AuditSqlOptions());

        collector.Add("SELECT 1", null, 300);
        collector.Add("SELECT 2", null, 500);

        var snapshot = collector.Close();

        Assert.Equal(2, snapshot.Count);
        Assert.Equal(1, snapshot.SlowCount);
        Assert.Single(snapshot.Items);
        Assert.Equal(500, snapshot.Items[0].ElapsedMs);
    }

    [Fact]
    public void 业务编码参数不被掩码_验证码参数被掩码()
    {
        var collector = new AuditSqlCollector(new AuditSqlOptions { RecordAll = true });
        collector.Add(
            "INSERT INTO Menu(PermissionCode) VALUES(@permissionCode)",
            Params(("@permissionCode", "menu:list"), ("@roleCode", "ops"), ("@code", "9527")),
            1);

        var parameters = collector.Close().Items[0].Parameters;

        Assert.Contains("@permissionCode=\"menu:list\"", parameters);
        Assert.Contains("@roleCode=\"ops\"", parameters);
        Assert.Contains("@code=***", parameters);
        Assert.DoesNotContain("9527", parameters);
    }

    [Fact]
    public void 执行失败的SQL即使未超阈值也记录并带失败原因()
    {
        var collector = new AuditSqlCollector(new AuditSqlOptions { SlowThresholdMs = 200 });

        collector.Add("SELECT ok", null, 5);
        collector.Add("INSERT INTO Menu(Name) VALUES(@name)", Params(("@name", "x")), 5,
            "Duplicate entry 'x' for key 'Menu'");

        var snapshot = collector.Close();

        Assert.Equal(2, snapshot.Count);
        Assert.Single(snapshot.Items);
        Assert.False(snapshot.Items[0].IsSlow);
        Assert.Contains("Duplicate entry", snapshot.Items[0].ErrorMessage);
    }

    [Fact]
    public void 敏感参数在拼接参数列表前即被掩码()
    {
        var collector = new AuditSqlCollector(new AuditSqlOptions { RecordAll = true });
        collector.Add(
            "UPDATE UserInfo SET Password=@newPassword WHERE Id=@id",
            Params(("@newPassword", "S3cretPass!987"), ("@verificationCode", "9527"), ("@id", 12)),
            1);

        var snapshot = collector.Close();
        var parameters = snapshot.Items[0].Parameters;

        Assert.DoesNotContain("S3cretPass!987", parameters);
        Assert.DoesNotContain("9527", parameters);
        Assert.Contains("@newPassword=***", parameters);
        Assert.Contains("@verificationCode=***", parameters);
        Assert.Contains("@id=12", parameters);
    }

    [Fact]
    public void SQL文本与参数各自按配置截断()
    {
        var collector = new AuditSqlCollector(new AuditSqlOptions
        {
            RecordAll = true,
            MaxSqlLength = 100,
            MaxParametersLength = 100
        });

        collector.Add(new string('S', 500), Params(("@value", new string('P', 500))), 1);

        var snapshot = collector.Close();
        var item = snapshot.Items[0];

        Assert.True(item.SqlText.Length <= 100);
        Assert.Contains("(truncated)", item.SqlText);
        Assert.True(item.Parameters.Length <= 100);
        Assert.Contains("(truncated)", item.Parameters);
    }

    [Fact]
    public void 明细条数达到上限后追加已截断标记()
    {
        var collector = new AuditSqlCollector(new AuditSqlOptions
        {
            RecordAll = true,
            MaxItemsPerAuditLog = 2
        });

        for (var i = 0; i < 5; i++) collector.Add($"SELECT {i}", null, i);

        var snapshot = collector.Close();

        Assert.Equal(5, snapshot.Count);
        Assert.Equal(3, snapshot.Items.Count);
        Assert.Contains("已截断", snapshot.Items[^1].SqlText);
        Assert.Contains("3 条", snapshot.Items[^1].SqlText);
    }

    [Fact]
    public void 关闭后到达的SQL不再计入()
    {
        var collector = new AuditSqlCollector(new AuditSqlOptions { SlowThresholdMs = 200 });
        collector.Add("SELECT 1", null, 300);
        collector.Close();
        collector.Add("SELECT 2", null, 1);

        Assert.True(collector.IsClosed);
        var snapshot = collector.Close();
        Assert.Equal(1, snapshot.Count);
    }

    [Fact]
    public void 无HttpContext时不采集且不同请求互不串号()
    {
        var accessor = new HttpContextAccessor();

        // 后台/定时任务没有 HttpContext：直接跳过
        AuditSqlCapture.Capture("SELECT background", null, TimeSpan.FromMilliseconds(1), accessor);
        Assert.Null(accessor.HttpContext);

        var context1 = new DefaultHttpContext();
        var context2 = new DefaultHttpContext();
        context1.Items[AuditLogContextKeys.SqlCollector] = new AuditSqlCollector(new AuditSqlOptions());
        context2.Items[AuditLogContextKeys.SqlCollector] = new AuditSqlCollector(new AuditSqlOptions());

        accessor.HttpContext = context1;
        AuditSqlCapture.Capture("SELECT a", null, TimeSpan.FromMilliseconds(600), accessor);

        accessor.HttpContext = context2;
        AuditSqlCapture.Capture("SELECT b", null, TimeSpan.FromMilliseconds(600), accessor);

        var first = ((AuditSqlCollector)context1.Items[AuditLogContextKeys.SqlCollector]).Close();
        var second = ((AuditSqlCollector)context2.Items[AuditLogContextKeys.SqlCollector]).Close();

        Assert.Equal(1, first.Count);
        Assert.Equal("SELECT a", first.Items[0].SqlText);
        Assert.Equal(1, second.Count);
        Assert.Equal("SELECT b", second.Items[0].SqlText);
    }

    [Fact]
    public void 请求结束后关闭的缓冲区不再接收迟到的SQL()
    {
        var context = new DefaultHttpContext();
        var collector = new AuditSqlCollector(new AuditSqlOptions());
        context.Items[AuditLogContextKeys.SqlCollector] = collector;

        var accessor = new HttpContextAccessor { HttpContext = context };
        collector.Close();

        AuditSqlCapture.Capture("SELECT late", null, TimeSpan.FromMilliseconds(300), accessor);

        Assert.Equal(0, collector.Close().Count);
    }
}
