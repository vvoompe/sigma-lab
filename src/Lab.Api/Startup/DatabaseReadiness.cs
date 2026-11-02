using System.Data.Common;
using Lab.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Lab.Api.Startup;

/// <summary>Перевіряє не лише порт PostgreSQL, а доступність мігрованої схеми API.</summary>
internal sealed class DatabaseReadiness(IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            _ = await db.Sites.AsNoTracking().AnyAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (DbException)
        {
            // Health endpoint не розкриває рядок підключення, SQL або виняток.
            return HealthCheckResult.Unhealthy();
        }
    }
}
