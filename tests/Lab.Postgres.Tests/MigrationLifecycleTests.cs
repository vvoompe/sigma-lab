using System.Data.Common;
using Lab.Domain.Sites;
using Lab.Infrastructure.Persistence;
using Lab.Postgres.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Lab.Postgres.Tests;

[Collection(PostgresTestGroup.Name)]
public sealed class MigrationLifecycleTests
{
    [Fact]
    public async Task SqliteMigrations_CanRollBack_AndRecreateAnEmptySchema()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sigma-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<LabDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory, "lab.db")};Pooling=False",
                    sqlite => sqlite.MigrationsAssembly("Lab.Migrations.Sqlite"));
            await using var database = new LabDbContext(options.Options);
            await VerifyRoundTripAsync(database);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [PostgresFact]
    public async Task PostgresMigrations_CanRollBack_AndRecreateAnEmptySchema()
    {
        // Окрема база: rollback не торкається спільних тестових або локальних даних.
        var connection = new NpgsqlConnectionStringBuilder(PostgresTestDatabase.ConnectionString)
        {
            Database = "sigma_migration_" + Guid.NewGuid().ToString("N"),
        };
        var options = new DbContextOptionsBuilder<LabDbContext>()
            .UseNpgsql(connection.ConnectionString, postgres => postgres.MigrationsAssembly("Lab.Migrations.Postgres"));
        await using var database = new LabDbContext(options.Options);
        try
        {
            await VerifyRoundTripAsync(database);
        }
        finally
        {
            await database.Database.EnsureDeletedAsync();
        }
    }

    private static async Task VerifyRoundTripAsync(LabDbContext database)
    {
        await database.Database.MigrateAsync();
        var applied = (await database.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.NotEmpty(applied);
        Assert.False(database.Database.HasPendingModelChanges());
        var owner = new Lab.Domain.Users.User("Дослідник", "owner@example.org");
        database.Add(owner);
        database.Entry(owner).Property(user => user.Id).CurrentValue = 42;
        await database.SaveChangesAsync();
        database.Sites.Add(new Site { Name = "Відкат міграції", OwnerId = 42, CreatedAt = DateTime.UtcNow });
        await database.SaveChangesAsync();
        Assert.Equal(42, (await database.Sites.AsNoTracking().SingleAsync()).OwnerId);

        var migrator = database.GetService<IMigrator>();
        await migrator.MigrateAsync("0");
        Assert.Empty(await database.Database.GetAppliedMigrationsAsync());
        await Assert.ThrowsAnyAsync<DbException>(() => database.Sites.AsNoTracking().ToListAsync());

        await migrator.MigrateAsync();
        Assert.Equal(applied, await database.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await database.Sites.AsNoTracking().ToListAsync());
        Assert.False(database.Database.HasPendingModelChanges());
    }
}
