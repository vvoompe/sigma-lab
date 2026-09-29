using Lab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lab.Migrations.Postgres;

/// <summary>
/// Точка входу для <c>dotnet ef</c> у проєкті міграцій PostgreSQL. Рядок підключення тут
/// не потрібен для генерації: EF Core будує DDL із моделі, а не з живої бази.
/// </summary>
public sealed class PostgresDesignTimeDbContextFactory : IDesignTimeDbContextFactory<LabDbContext>
{
    /// <summary>Змінна середовища, яка перекриває рядок підключення для команд `dotnet ef`.</summary>
    public const string ConnectionStringEnvironmentVariable = "LAB_POSTGRES_CONNECTION";

    /// <summary>
    /// Рядок підключення за замовчуванням для часу проєктування. Генерація міграцій не
    /// звертається до бази, але `dotnet ef database update` - так, тому для іншого порту
    /// чи іншої бази задайте змінну <see cref="ConnectionStringEnvironmentVariable"/>.
    /// </summary>
    public const string DefaultConnectionString =
        "Host=localhost;Port=55432;Database=lab;Username=lab;Password=lab_design";

    /// <summary>Рядок підключення з урахуванням змінної середовища.</summary>
    public static string ResolveConnectionString() =>
        Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
        ?? DefaultConnectionString;

    /// <inheritdoc />
    public LabDbContext CreateDbContext(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var builder = new DbContextOptionsBuilder<LabDbContext>();
        builder.UseNpgsql(
            ResolveConnectionString(),
            npgsql => npgsql.MigrationsAssembly("Lab.Migrations.Postgres"));

        return new LabDbContext(builder.Options);
    }
}
