using Lab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lab.Postgres.Tests.Support;

/// <summary>
/// Хелпери для тестів на реальному PostgreSQL. Тести вмикаються змінною середовища,
/// тому <c>dotnet test</c> не падає на машині без Docker, а в CI виконуються по-справжньому.
/// </summary>
public static class PostgresTestDatabase
{
    /// <summary>Змінна середовища, яка вмикає тести на PostgreSQL.</summary>
    public const string EnableEnvironmentVariable = "LAB_TEST_POSTGRES";

    /// <summary>Змінна середовища з власним рядком підключення для тестів.</summary>
    public const string ConnectionStringEnvironmentVariable = "LAB_TEST_POSTGRES_CONNECTION";

    private const string DefaultConnectionString =
        "Host=localhost;Port=55432;Database=lab;Username=lab;Password=lab_dev_password";

    /// <summary>Чи ввімкнені тести на PostgreSQL.</summary>
    public static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable(EnableEnvironmentVariable), "1", StringComparison.Ordinal) ||
        string.Equals(Environment.GetEnvironmentVariable(EnableEnvironmentVariable), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Рядок підключення до тестової бази.</summary>
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
        ?? DefaultConnectionString;

    /// <summary>Створює контекст, підключений до тестової бази PostgreSQL.</summary>
    public static LabDbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<LabDbContext>();
        builder.UseNpgsql(
            ConnectionString,
            npgsql => npgsql.MigrationsAssembly("Lab.Migrations.Postgres"));

        return new LabDbContext(builder.Options);
    }

    /// <summary>
    /// Очищає таблиці засобами EF Core (жодного сирого SQL у тестах), щоб кожен тест
    /// починався з передбачуваного стану.
    /// </summary>
    public static async Task CleanAsync(LabDbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        await context.Sites.ExecuteDeleteAsync(cancellationToken);
    }
}

/// <summary>
/// Факт, який виконується лише за ввімкнених тестів PostgreSQL; інакше позначається
/// пропущеним. Реалізовано власним атрибутом, бо в xunit 2.x динамічний пропуск
/// робиться саме так (у xunit 3 є <c>Assert.Skip*</c>).
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    /// <summary>Створює атрибут і вирішує, чи тест буде пропущено.</summary>
    public PostgresFactAttribute()
    {
        if (!PostgresTestDatabase.IsEnabled)
        {
            Skip = $"PostgreSQL-тести вимкнено. Увімкнення: {PostgresTestDatabase.EnableEnvironmentVariable}=1 dotnet test";
        }
    }
}

/// <summary>
/// Колекція тестів на PostgreSQL: вони працюють з однією базою даних, тому виконуються
/// послідовно, щоб не заважати одне одному.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresTestGroup
{
    /// <summary>Назва колекції.</summary>
    public const string Name = "postgres";
}
