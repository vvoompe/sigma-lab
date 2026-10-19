using Lab.Domain.Sites;
using Lab.Domain.Audit;
using Lab.Domain.Security;
using Lab.Domain.Users;
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
            Assert.Equal(
                new[]
                {
                    "20260929104420_InitialCreate",
                    "20261015200112_DomainModel",
                    "20261015200437_SiteOwnership",
                    "20261017210144_SecurityCredentials",
                },
                applied);

            db.Sites.Add(new Site
            {
                Name = "Міграційний майданчик",
                Latitude = 50.4,
                Longitude = 30.5,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();

            Assert.Equal(1, await db.Sites.CountAsync());

            var passwordUpdatedAt = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
            var user = new User("Обліковий запис", "credentials@example.org");
            var credential = new UserCredential(user, "test-password-hash", passwordUpdatedAt, UserRole.LabManager);
            db.UserCredentials.Add(credential);
            await db.SaveChangesAsync();
            Assert.Equal(user.Id, credential.UserId);

            db.ChangeTracker.Clear();
            var storedCredential = await db.UserCredentials.Include(item => item.User).SingleAsync();
            Assert.NotNull(storedCredential.User);
            Assert.Equal(UserRole.LabManager, storedCredential.Role);
            Assert.Equal(passwordUpdatedAt, storedCredential.PasswordUpdatedAt);
            Assert.Equal(DateTimeKind.Utc, storedCredential.PasswordUpdatedAt.Kind);

            var occurredAt = passwordUpdatedAt.AddMinutes(1);
            db.AuditEntries.Add(new AuditEntry(
                storedCredential.UserId,
                "user.role.changed",
                "UserCredential",
                storedCredential.UserId,
                occurredAt));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var audit = await db.AuditEntries.SingleAsync();
            Assert.Equal(storedCredential.UserId, audit.ActorId);
            Assert.Equal(storedCredential.UserId, audit.TargetId);
            Assert.Equal(DateTimeKind.Utc, audit.OccurredAt.Kind);
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
                Assert.Equal(
                    new[]
                    {
                        "20260929104420_InitialCreate",
                        "20261015200112_DomainModel",
                        "20261015200437_SiteOwnership",
                        "20261017210144_SecurityCredentials",
                    },
                    await second.Database.GetAppliedMigrationsAsync());
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

    [Fact]
    public async Task Видалення_профілю_каскадно_видаляє_облікові_дані()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lab-test-{Guid.NewGuid():N}.db");

        try
        {
            await using var db = CreateContext(path);
            await db.Database.MigrateAsync();
            var user = new User("Каскадне видалення", "cascade@example.org");
            db.UserCredentials.Add(new UserCredential(user, "test-password-hash", DateTime.UtcNow));
            await db.SaveChangesAsync();

            db.Users.Remove(user);
            await db.SaveChangesAsync();

            Assert.Empty(await db.UserCredentials.ToArrayAsync());
        }
        finally
        {
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
