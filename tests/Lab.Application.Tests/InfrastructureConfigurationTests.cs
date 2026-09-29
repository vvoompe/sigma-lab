using Lab.Application.Abstractions;
using Lab.Application.DependencyInjection;
using Lab.Infrastructure.Configuration;
using Lab.Infrastructure.DependencyInjection;
using Lab.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lab.Application.Tests;

/// <summary>
/// Тести конфігурації доступу до даних і реєстрації в контейнері залежностей.
/// Саме тут перевіряється головна обіцянка архітектури: СУБД вибирає **конфігурація**,
/// а не код, і прикладна логіка бачить лише <see cref="IAppDbContext"/>.
/// </summary>
public sealed class InfrastructureConfigurationTests
{
    [Fact]
    public void Змінна_середовища_перекриває_значення_з_конфігурації()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["ConnectionStrings:Postgres"] = "Host=localhost;Port=55432;Database=lab",
        });

        WithEnvironment(DatabaseOptions.ProviderEnvironmentVariable, "Postgres", () =>
        {
            var options = DatabaseOptions.FromConfiguration(configuration);

            Assert.Equal(DatabaseProvider.Postgres, options.Provider);
            Assert.Equal("Lab.Migrations.Postgres", options.GetMigrationsAssembly());
            Assert.Equal("Host=localhost;Port=55432;Database=lab", options.GetConnectionString());
        });
    }

    [Fact]
    public void Без_змінної_середовища_провайдер_береться_з_конфігурації()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Postgres",
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=lab",
        });

        WithEnvironment(DatabaseOptions.ProviderEnvironmentVariable, null, () =>
        {
            var options = DatabaseOptions.FromConfiguration(configuration);

            Assert.Equal(DatabaseProvider.Postgres, options.Provider);
            Assert.Equal("Host=localhost;Database=lab", options.GetConnectionString());
        });
    }

    [Fact]
    public void Невідомий_провайдер_не_ламає_застосунок()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Oracle",
            ["ConnectionStrings:Sqlite"] = "Data Source=lab.db",
        });

        WithEnvironment(DatabaseOptions.ProviderEnvironmentVariable, null, () =>
        {
            var options = DatabaseOptions.FromConfiguration(configuration);

            // Свідоме рішення: невідоме значення не валить старт, а відкочується на SQLite.
            Assert.Equal(DatabaseProvider.Sqlite, options.Provider);
            Assert.Equal("Lab.Migrations.Sqlite", options.GetMigrationsAssembly());
        });
    }

    [Fact]
    public void Відсутній_рядок_підключення_дає_зрозумілу_помилку()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Postgres",
        });

        WithEnvironment(DatabaseOptions.ProviderEnvironmentVariable, null, () =>
        {
            var options = DatabaseOptions.FromConfiguration(configuration);

            var exception = Assert.Throws<InvalidOperationException>(() => options.GetConnectionString());
            Assert.Contains("ConnectionStrings:Postgres", exception.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Конфігурація_обов_язкова()
    {
        WithEnvironment(DatabaseOptions.ProviderEnvironmentVariable, null, () =>
            Assert.Throws<ArgumentNullException>(() => DatabaseOptions.FromConfiguration(null!)));
    }

    [Fact]
    public void Реєстрація_в_контейнері_віддає_інтерфейс_і_сервіс_для_SQLite()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["ConnectionStrings:Sqlite"] = "Data Source=:memory:",
        });

        WithEnvironment(DatabaseOptions.ProviderEnvironmentVariable, null, () =>
        {
            var services = new ServiceCollection();
            services.AddLabInfrastructure(configuration);
            services.AddLabApplication();

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            var context = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            Assert.IsType<LabDbContext>(context);
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<ISiteService>());

            // Провайдер обрано саме той, що заданий конфігурацією.
            Assert.Contains("Sqlite", ((LabDbContext)context).Database.ProviderName, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Реєстрація_в_контейнері_поважає_змінну_середовища_для_Postgres()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["ConnectionStrings:Sqlite"] = "Data Source=:memory:",
            ["ConnectionStrings:Postgres"] = "Host=localhost;Port=55432;Database=lab;Username=lab;Password=lab",
        });

        WithEnvironment(DatabaseOptions.ProviderEnvironmentVariable, "Postgres", () =>
        {
            var services = new ServiceCollection();
            services.AddLabInfrastructure(configuration);

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            var context = scope.ServiceProvider.GetRequiredService<LabDbContext>();
            Assert.Contains("Npgsql", context.Database.ProviderName, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Аргументи_реєстрації_перевіряються()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration([]);

        Assert.Throws<ArgumentNullException>(() => services.AddLabInfrastructure(null!));
        Assert.Throws<ArgumentNullException>(() =>
            Lab.Infrastructure.DependencyInjection.LabInfrastructureServiceCollectionExtensions
                .AddLabInfrastructure(null!, configuration));
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    /// <summary>
    /// Виставляє змінну середовища на час тесту й повертає попереднє значення, щоб тести
    /// не впливали один на одного.
    /// </summary>
    private static void WithEnvironment(string name, string? value, Action action)
    {
        var previous = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, value);
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, previous);
        }
    }
}
