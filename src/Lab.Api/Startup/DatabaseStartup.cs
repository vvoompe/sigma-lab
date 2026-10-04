using Lab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lab.Api.Startup;

/// <summary>Застосування міграцій під час старту застосунку.</summary>
public static partial class DatabaseStartup
{
    /// <summary>Застосовує всі неприйняті міграції для вибраної СУБД.</summary>
    public static async Task ApplyMigrationsAsync(IServiceProvider services, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();

        await db.Database.MigrateAsync();

        var applied = (await db.Database.GetAppliedMigrationsAsync()).Count();
        LogMigrationApplied(logger, applied, db.Database.ProviderName ?? "невідомий провайдер");
    }

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Міграції застосовано. Прийнятих міграцій: {Count}, провайдер: {Provider}")]
    private static partial void LogMigrationApplied(ILogger logger, int count, string provider);
}
