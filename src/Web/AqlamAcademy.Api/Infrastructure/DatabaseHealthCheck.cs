using AqlamAcademy.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AqlamAcademy.Api.Infrastructure;

/// <summary>Ready only when the platform database answers. Used by /health/ready, not by the cheap liveness probe.</summary>
public class DatabaseHealthCheck(ApplicationDbContext context) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext healthContext, CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database is not reachable.");
        }
        catch (Exception)
        {
            // Details stay in the logs; the public answer never explains what is wrong.
            return HealthCheckResult.Unhealthy("Database check failed.");
        }
    }
}
