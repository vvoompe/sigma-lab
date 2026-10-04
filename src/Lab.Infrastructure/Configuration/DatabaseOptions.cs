using Microsoft.Extensions.Configuration;

namespace Lab.Infrastructure.Configuration;

/// <summary>
/// Вибір СУБД і рядків підключення з конфігурації.
/// Змінна середовища <c>LAB_DB_PROVIDER</c> має вищий пріоритет за ключ
/// <c>Database:Provider</c> з <c>appsettings.json</c> - саме завдяки цьому СУБД
/// перемикається без зміни коду й без перезбірки.
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>Ім'я змінної середовища, яка перекриває провайдера з конфігурації.</summary>
    public const string ProviderEnvironmentVariable = "LAB_DB_PROVIDER";

    /// <summary>Вибрана СУБД.</summary>
    public DatabaseProvider Provider { get; init; } = DatabaseProvider.Sqlite;

    /// <summary>Рядок підключення до SQLite.</summary>
    public string? SqliteConnectionString { get; init; }

    /// <summary>Рядок підключення до PostgreSQL.</summary>
    public string? PostgresConnectionString { get; init; }

    /// <summary>Читає налаштування з конфігурації застосунку.</summary>
    public static DatabaseOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var fromEnvironment = Environment.GetEnvironmentVariable(ProviderEnvironmentVariable);
        var raw = string.IsNullOrWhiteSpace(fromEnvironment)
            ? configuration["Database:Provider"]
            : fromEnvironment;

        var provider = Enum.TryParse<DatabaseProvider>(raw, ignoreCase: true, out var parsed)
            ? parsed
            : DatabaseProvider.Sqlite;

        return new DatabaseOptions
        {
            Provider = provider,
            SqliteConnectionString = configuration.GetConnectionString("Sqlite"),
            PostgresConnectionString = configuration.GetConnectionString("Postgres"),
        };
    }

    /// <summary>Рядок підключення для вибраної СУБД.</summary>
    public string GetConnectionString() => Provider switch
    {
        DatabaseProvider.Postgres => PostgresConnectionString
            ?? throw new InvalidOperationException("Не задано рядок підключення ConnectionStrings:Postgres."),
        _ => SqliteConnectionString
            ?? throw new InvalidOperationException("Не задано рядок підключення ConnectionStrings:Sqlite."),
    };

    /// <summary>
    /// Assembly з міграціями для вибраної СУБД. Тримається рядком, щоб інфраструктура
    /// не залежала від проєктів міграцій: залежність іде тільки в один бік.
    /// </summary>
    public string GetMigrationsAssembly() => Provider switch
    {
        DatabaseProvider.Postgres => "Lab.Migrations.Postgres",
        _ => "Lab.Migrations.Sqlite",
    };
}
