using LogicNet.Application.Audit.DTO;
using LogicNet.Application.Audit.Services;
using LogicNet.Core.Entity;
using SqlSugar;
using Xunit;

namespace LogicNet.Tests;

/// <summary>
///     审计查询与清理：过滤条件、详情脱敏、分页钳制、按天数删除
/// </summary>
public class AuditLogServiceTests
{
    private static AuditLogService CreateService(SqlSugarScope db)
    {
        db.CodeFirst.InitTables(typeof(AuditLog));
        return new AuditLogService(db);
    }

    private static AuditLog Seed(SqlSugarScope db, long id, long? userId, string userName, string path,
        DateTime createTime, bool succeeded = true, string httpMethod = "POST", string requestBody = null)
    {
        var log = new AuditLog
        {
            Id = id,
            UserId = userId,
            UserName = userName,
            HttpMethod = httpMethod,
            Path = path,
            RequestBody = requestBody,
            StatusCode = succeeded ? 200 : 500,
            ResultCode = succeeded ? 200 : 500,
            Succeeded = succeeded,
            DurationMs = 10,
            CreateTime = createTime
        };
        db.Insertable(log).ExecuteCommand();
        // BaseEntity 的 CreateTime 标记了 InsertServerTime，插入时会被改写为当前时间；
        // 测试需要历史时间，插入后再显式回写（生产环境同样由服务端时间填充）
        db.Updateable<AuditLog>()
            .SetColumns(x => x.CreateTime == createTime)
            .Where(x => x.Id == id)
            .ExecuteCommand();
        return log;
    }

    private static void SeedFive(SqlSugarScope db)
    {
        var now = DateTime.Now;
        Seed(db, 1, 1, "admin", "/Api/Menu/CreateMenu", now.AddDays(-40));
        Seed(db, 2, 1, "admin", "/Api/UserInfo/Login", now.AddDays(-20));
        Seed(db, 3, 2, "user01", "/Api/Menu/DeleteMenu", now.AddDays(-5), false, "DELETE");
        Seed(db, 4, 2, "user01", "/Api/Menu/UpdateMenu", now.AddDays(-1), true, "PUT");
        Seed(db, 5, 1, "admin", "/Api/Test/ChangeWordContent", now, false, "GET");
    }

    [Fact]
    public async Task 支持时间区间_用户_路径_成功状态_方法过滤()
    {
        using var db = TestDb.Create();
        var service = CreateService(db);
        SeedFive(db);

        var byTime = await service.GetAuditLogListAsync(new GetAuditLogListInputDto
        {
            StartTime = DateTime.Now.AddDays(-10),
            EndTime = DateTime.Now.AddMinutes(1)
        });
        Assert.Equal(3, byTime.Data.Count);

        var byUser = await service.GetAuditLogListAsync(new GetAuditLogListInputDto { UserId = 2 });
        Assert.Equal(2, byUser.Data.Count);

        var byPath = await service.GetAuditLogListAsync(new GetAuditLogListInputDto { Path = "Menu" });
        Assert.Equal(3, byPath.Data.Count);

        var bySucceeded = await service.GetAuditLogListAsync(new GetAuditLogListInputDto { Succeeded = false });
        Assert.Equal(2, bySucceeded.Data.Count);

        var byMethod = await service.GetAuditLogListAsync(new GetAuditLogListInputDto { HttpMethod = "delete" });
        Assert.Single(byMethod.Data);
        Assert.Equal("DELETE", byMethod.Data[0].HttpMethod);

        var byUserName = await service.GetAuditLogListAsync(new GetAuditLogListInputDto { UserName = "user" });
        Assert.Equal(2, byUserName.Data.Count);
    }

    [Fact]
    public async Task 列表按时间倒序且不返回请求体()
    {
        using var db = TestDb.Create();
        var service = CreateService(db);
        SeedFive(db);

        var page = await service.GetAuditLogListAsync(new GetAuditLogListInputDto { PageSize = 100 });

        Assert.Equal(new[] { 5L, 4L, 3L, 2L, 1L }, page.Data.Select(x => x.Id).ToArray());
        Assert.Equal(5, page.TotalCount);
    }

    [Fact]
    public async Task 详情返回已脱敏请求体且分页被钳制()
    {
        using var db = TestDb.Create();
        var service = CreateService(db);
        var maskedBody = "{\"passWord\":\"***\",\"code\":\"***\"}";
        Seed(db, 10, 1, "admin", "/Api/UserInfo/Login", DateTime.Now, true, "POST", maskedBody);

        var detail = await service.GetAuditLogDetailAsync(10);
        Assert.Equal(maskedBody, detail.RequestBody);
        Assert.Equal("admin", detail.UserName);

        var clamped = await service.GetAuditLogListAsync(new GetAuditLogListInputDto { PageSize = 0 });
        Assert.Equal(1, clamped.PageSize);
        Assert.Single(clamped.Data);
    }

    [Fact]
    public async Task 详情不存在时给出可读错误()
    {
        using var db = TestDb.Create();
        var service = CreateService(db);

        await Assert.ThrowsAnyAsync<Exception>(() => service.GetAuditLogDetailAsync(999));
    }

    [Fact]
    public async Task 清理按天数只删除过期记录()
    {
        using var db = TestDb.Create();
        var service = CreateService(db);
        Seed(db, 21, 1, "admin", "/Api/A", DateTime.Now.AddDays(-40));
        Seed(db, 22, 1, "admin", "/Api/B", DateTime.Now.AddDays(-31));
        Seed(db, 23, 1, "admin", "/Api/C", DateTime.Now.AddDays(-10));

        var deleted = await service.CleanupAuditLogsAsync(30);

        Assert.Equal(2, deleted);
        Assert.Single(db.Queryable<AuditLog>().ToList());
    }

    [Fact]
    public async Task 非法保留天数被拒绝()
    {
        using var db = TestDb.Create();
        var service = CreateService(db);

        await Assert.ThrowsAnyAsync<Exception>(() => service.CleanupAuditLogsAsync(0));
        await Assert.ThrowsAnyAsync<Exception>(() => service.CleanupAuditLogsAsync(3651));
    }
}
