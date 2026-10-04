using Lab.Domain.Sites;
using Lab.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Lab.Application.Tests;

/// <summary>
/// Тести міграцій SQLite: схема створюється з нуля міграціями (а не EnsureCreated),
/// на тимчасовому файлі, який прибирається після прогону.
/// </summary>
public sealed class SqliteMigrationTests
{
    [Fact]
    public async Task Міграції_створюють_схему_і_дані_зберігаються()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lab-test-{Guid.NewGuid():N}.db");

        try
        {
            await using var db = CreateContext(path);

            await db.Database.MigrateAsync();

            var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
            Assert.Single(applied);

            db.Sites.Add(new Site
            {
                Name = "Міграційний майданчик",
                Latitude = 50.4,
                Longitude = 30.5,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();

            Assert.Equal(1, await db.Sites.CountAsync());
        }
        finally
        {
            // Microsoft.Data.Sqlite тримає файл у пулі з'єднань навіть після Dispose,
            // тому без очищення пулу видалення файлу падає з «being used by another process».
            SqliteConnection.ClearAllPools();

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Повторне_застосування_міграцій_нічого_не_ламає()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lab-test-{Guid.NewGuid():N}.db");

        try
        {
            await using (var first = CreateContext(path))
            {
                await first.Database.MigrateAsync();
            }

            await using (var second = CreateContext(path))
            {
                // Ідемпотентність: друга спроба не створює дублів і не падає.
                await second.Database.MigrateAsync();
                Assert.Single(await second.Database.GetAppliedMigrationsAsync());
            }
        }
        finally
        {
            // Microsoft.Data.Sqlite тримає файл у пулі з'єднань навіть після Dispose,
            // тому без очищення пулу видалення файлу падає з «being used by another process».
            SqliteConnection.ClearAllPools();

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static LabDbContext CreateContext(string path)
    {
        var options = new DbContextOptionsBuilder<LabDbContext>()
            .UseSqlite($"Data Source={path}", sqlite => sqlite.MigrationsAssembly("Lab.Migrations.Sqlite"))
            .Options;

        return new LabDbContext(options);
    }
}
