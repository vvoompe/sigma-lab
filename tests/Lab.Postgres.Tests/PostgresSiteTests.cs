using Lab.Domain.Sites;
using Lab.Infrastructure.Configuration;
using Lab.Postgres.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Lab.Postgres.Tests;

/// <summary>
/// Тести проти реальної PostgreSQL. Виконуються лише з <c>LAB_TEST_POSTGRES=1</c>
/// (локально: після `docker compose -f docker/dev-postgres.yml up -d`), у CI - завжди.
/// Без змінної позначаються пропущеними, тому `dotnet test` працює й без Docker.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class PostgresSiteTests
{
    [PostgresFact]
    public async Task Міграції_застосовуються_і_майданчик_зберігається()
    {
        await using var db = PostgresTestDatabase.CreateContext();
        await db.Database.MigrateAsync();

        var site = new Site
        {
            Name = $"Тест {Guid.NewGuid():N}",
            Latitude = 50.45,
            Longitude = 30.52,
            CreatedAt = DateTime.UtcNow,
        };

        db.Sites.Add(site);
        await db.SaveChangesAsync();

        try
        {
            Assert.True(site.Id > 0);

            var stored = await db.Sites.AsNoTracking().SingleAsync(item => item.Id == site.Id);
            Assert.Equal(site.Name, stored.Name);
            Assert.Equal(site.Latitude, stored.Latitude);
        }
        finally
        {
            await PostgresTestDatabase.CleanAsync(db);
        }
    }

    [PostgresFact]
    public async Task Схема_після_міграцій_містить_таблицю_майданчиків()
    {
        await using var db = PostgresTestDatabase.CreateContext();
        await db.Database.MigrateAsync();

        var tables = await db.Database
            .SqlQueryRaw<string>(
                "SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public'")
            .ToListAsync();

        Assert.Contains("sites", tables);
        Assert.Contains("__EFMigrationsHistory", tables);
    }

    [PostgresFact]
    public async Task Очищення_таблиць_виконується_засобами_EF_Core()
    {
        await using var db = PostgresTestDatabase.CreateContext();
        await db.Database.MigrateAsync();

        db.Sites.Add(new Site
        {
            Name = $"Тимчасовий {Guid.NewGuid():N}",
            Latitude = 50.0,
            Longitude = 30.0,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        await PostgresTestDatabase.CleanAsync(db);

        Assert.Empty(await db.Sites.AsNoTracking().ToListAsync());
    }

    [Fact]
    public void Перелік_провайдерів_не_змінився()
    {
        // Дешева перевірка, що перелік провайдерів не «поплив» під час рефакторингу.
        Assert.Equal(DatabaseProvider.Sqlite, Enum.Parse<DatabaseProvider>("Sqlite", ignoreCase: true));
        Assert.Equal(DatabaseProvider.Postgres, Enum.Parse<DatabaseProvider>("Postgres", ignoreCase: true));
    }
}
