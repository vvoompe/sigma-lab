using Lab.Application.Security;
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

    public static async Task SeedInitialAdministratorAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        await using var scope = services.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IInitialAdministratorSeeder>();
        await seeder.SeedAsync(
            configuration["Seed:AdminEmail"],
            configuration["Seed:AdminPassword"],
            cancellationToken);
    }

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Міграції застосовано. Прийнятих міграцій: {Count}, провайдер: {Provider}")]
    private static partial void LogMigrationApplied(ILogger logger, int count, string provider);
}
