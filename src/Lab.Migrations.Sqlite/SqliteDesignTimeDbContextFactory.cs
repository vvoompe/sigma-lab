using Lab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lab.Migrations.Sqlite;

/// <summary>
/// Точка входу для <c>dotnet ef</c>: провайдер і assembly міграцій задані жорстко, тому
/// застосувати до SQLite набір міграцій від PostgreSQL неможливо навіть випадково.
/// </summary>
public sealed class SqliteDesignTimeDbContextFactory : IDesignTimeDbContextFactory<LabDbContext>
{
    /// <summary>Змінна середовища, яка перекриває рядок підключення для команд `dotnet ef`.</summary>
    public const string ConnectionStringEnvironmentVariable = "LAB_SQLITE_CONNECTION";

    /// <summary>
    /// Рядок підключення за замовчуванням. Важливо знати: `dotnet ef` виконується з робочим
    /// каталогом проєкту, тому файл створюється **в теці проєкту міграцій**, а не в корені
    /// репозиторію. Щоб працювати з конкретним файлом (наприклад, тим самим, що використовує
    /// API), задайте абсолютний шлях у змінній <see cref="ConnectionStringEnvironmentVariable"/>.
    /// </summary>
    public const string DefaultConnectionString = "Data Source=lab.db";

    /// <summary>Рядок підключення з урахуванням змінної середовища.</summary>
    public static string ResolveConnectionString() =>
        Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__Sqlite")
        ?? DefaultConnectionString;

    /// <inheritdoc />
    public LabDbContext CreateDbContext(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var builder = new DbContextOptionsBuilder<LabDbContext>();
        builder.UseSqlite(
            ResolveConnectionString(),
            sqlite => sqlite.MigrationsAssembly("Lab.Migrations.Sqlite"));

        return new LabDbContext(builder.Options);
    }
}
