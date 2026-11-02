using System.Security.Claims;
using System.Threading.Tasks;
using Furion.Authorization;
using LogicNet.Core.Entity;
using LogicNet.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;

namespace LogicNet.Web.Core;

public class JwtHandler : AppAuthorizeHandler
{
    public override async Task<bool> PipelineAsync(AuthorizationHandlerContext context, DefaultHttpContext httpContext)
    {
        // 令牌必须携带有效用户标识
        if (!long.TryParse(httpContext.User.FindFirstValue(JwtClaimNames.UserId), out var userId) || userId <= 0)
            return false;

        // 用户必须真实存在且未被软删除（软删除由全局查询过滤器保证过滤）
        var db = httpContext.RequestServices.GetService<ISqlSugarClient>();
        if (db == null) return false;
        return await db.Queryable<UserInfo>().AnyAsync(x => x.Id == userId);
    }
}
