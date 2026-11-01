using AqlamAcademy.Infrastructure.Persistence.MultiTenancy;
using Finbuckle.MultiTenant.AspNetCore.Extensions;
using Finbuckle.MultiTenant.Strategies;

namespace AqlamAcademy.Api.Infrastructure;

public static class HostTenantExtensions
{
    /// <summary>
    /// The organization id when the request came in on an organization's own (sub)domain, otherwise null.
    /// Resolution through the {org} route on the main domain does not count: sessions are bound to hosts, not routes.
    /// </summary>
    public static string? GetHostTenantId(this HttpContext httpContext)
    {
        var context = httpContext.GetMultiTenantContext<AqlamTenantInfo>();
        return context is { IsResolved: true } && context.StrategyInfo?.StrategyType == typeof(DelegateStrategy)
            ? context.TenantInfo!.Id
            : null;
    }
}
