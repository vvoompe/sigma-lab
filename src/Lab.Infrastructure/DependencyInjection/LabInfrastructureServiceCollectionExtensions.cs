using Lab.Application.Abstractions;
using Lab.Infrastructure.Configuration;
using Lab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lab.Infrastructure.DependencyInjection;

/// <summary>Реєстрація доступу до даних.</summary>
public static class LabInfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Додає <see cref="LabDbContext"/> і вибирає провайдера з конфігурації.
    /// Це ЄДИНЕ місце в solution, де згадуються конкретні СУБД: <c>UseSqlite</c> і
    /// <c>UseNpgsql</c> більше ніде не зустрічаються.
    /// </summary>
    public static IServiceCollection AddLabInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = DatabaseOptions.FromConfiguration(configuration);
        var connectionString = options.GetConnectionString();
        var migrationsAssembly = options.GetMigrationsAssembly();

        services.AddSingleton(options);
        services.AddDbContext<LabDbContext>(builder =>
        {
            if (options.Provider == DatabaseProvider.Postgres)
            {
                builder.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(migrationsAssembly));
            }
            else
            {
                builder.UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(migrationsAssembly));
            }
        });

        // Прикладна логіка залежить від інтерфейсу, а не від конкретного контексту.
        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<LabDbContext>());

        return services;
    }
}
