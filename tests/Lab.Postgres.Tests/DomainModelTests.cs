using Lab.Domain.Experiments;
using Lab.Domain.Instruments;
using Lab.Domain.Measurements;
using Lab.Domain.ReferenceValues;
using Lab.Domain.Sites;
using Lab.Domain.Users;
using Lab.Infrastructure.Persistence;
using Lab.Postgres.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Lab.Postgres.Tests;

[Collection(PostgresTestGroup.Name)]
public sealed class DomainModelTests : IAsyncLifetime
{
    private LabDbContext? postgresDatabase;

    [PostgresFact]
    public async Task PostgreSQL_зберігає_повну_модель_і_UTC()
    {
        var db = await CreatePostgresAsync();
        var series = await AddSeriesAsync(db);
        series.AddMeasurement(Guid.NewGuid(), 20, "°C", DateTime.UtcNow);
        series.Sign(series.OwnerId, DateTime.UtcNow);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var stored = await db.Set<Experiment>().Include(item => item.Instrument).Include(item => item.Measurements).SingleAsync();
        Assert.Equal(ExperimentStatus.SignedOff, stored.Status);
        Assert.Equal("°C", stored.Unit);
        Assert.Equal(DateTimeKind.Utc, Assert.Single(stored.Measurements).MeasuredAt.Kind);
        Assert.Equal(DateTimeKind.Utc, stored.SignedAt?.Kind);
        Assert.Equal(DateTimeKind.Utc, (await db.Sites.AsNoTracking().SingleAsync()).CreatedAt.Kind);
        Assert.False(db.Database.HasPendingModelChanges());
        db.ChangeTracker.Clear();
        var owner = await db.Set<User>().SingleAsync();
        db.Remove(owner);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task PostgreSQL_глобальний_ClientId_не_дублюється()
    {
        var db = await CreatePostgresAsync();
        var first = await AddSeriesAsync(db);
        var second = new Experiment(first.SiteId, first.OwnerId, "Друга", first.Instrument ?? throw new InvalidOperationException());
        db.Add(second);
        var id = Guid.NewGuid();
        first.AddMeasurement(id, 20, "°C", DateTime.UtcNow);
        second.AddMeasurement(id, 21, "°C", DateTime.UtcNow);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        // SaveChanges є транзакцією: жоден із двох конфліктних вимірів не збережено.
        Assert.Equal(0, await db.Set<Measurement>().CountAsync());
    }

    [PostgresFact]
    public async Task PostgreSQL_підпис_виграє_гонку_зі_старою_версією()
    {
        var db = await CreatePostgresAsync();
        var series = await AddSeriesAsync(db);
        series.AddMeasurement(Guid.NewGuid(), 20, "°C", DateTime.UtcNow);
        await db.SaveChangesAsync();
        await using var second = new LabDbContext(new DbContextOptionsBuilder<LabDbContext>()
            .UseNpgsql(db.Database.GetConnectionString(), pg => pg.MigrationsAssembly("Lab.Migrations.Postgres")).Options);
        var stale = await second.Set<Experiment>().Include(item => item.Instrument).Include(item => item.Measurements).SingleAsync();
        series.Sign(series.OwnerId, DateTime.UtcNow);
        await db.SaveChangesAsync();
        stale.AddMeasurement(Guid.NewGuid(), 21, "°C", DateTime.UtcNow);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        Assert.Equal(1, await db.Set<Measurement>().CountAsync());
    }

    [Fact]
    public async Task SQLite_оновлення_та_відкат_не_втрачають_старого_власника()
    {
        await using var db = new LabDbContext(new DbContextOptionsBuilder<LabDbContext>()
            .UseSqlite("Data Source=:memory:", sqlite => sqlite.MigrationsAssembly("Lab.Migrations.Sqlite")).Options);
        await db.Database.OpenConnectionAsync();
        await VerifyLegacyUpgradeAsync(db);
    }

    [PostgresFact]
    public async Task PostgreSQL_оновлення_та_відкат_не_втрачають_старого_власника()
    {
        var db = await CreatePostgresAsync(migrate: false);
        await VerifyLegacyUpgradeAsync(db);
    }

    [PostgresFact]
    public async Task PostgreSQL_редагування_зберігає_значення_та_версії()
    {
        var db = await CreatePostgresAsync();
        var series = await AddSeriesAsync(db);
        series.AddMeasurement(Guid.NewGuid(), 20, "°C", DateTime.UtcNow);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var loaded = await db.Set<Experiment>().Include(item => item.Instrument).Include(item => item.Measurements).SingleAsync();
        var version = loaded.Version;
        Assert.Single(loaded.Measurements).Update(25, "°C", DateTime.UtcNow);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var stored = await db.Set<Experiment>().Include(item => item.Measurements).SingleAsync();
        Assert.Equal(version + 1, stored.Version);
        Assert.Equal(2, Assert.Single(stored.Measurements).Version);
        Assert.Equal(25, Assert.Single(stored.Measurements).Value);
    }

    [Fact]
    public async Task SQLite_відкат_непорожньої_моделі_зберігає_майданчик()
    {
        await using var db = new LabDbContext(new DbContextOptionsBuilder<LabDbContext>()
            .UseSqlite("Data Source=:memory:", sqlite => sqlite.MigrationsAssembly("Lab.Migrations.Sqlite")).Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.MigrateAsync();
        await VerifyPopulatedRollbackAsync(db);
    }

    [PostgresFact]
    public async Task PostgreSQL_відкат_непорожньої_моделі_зберігає_майданчик()
    {
        await VerifyPopulatedRollbackAsync(await CreatePostgresAsync());
    }

    private static async Task VerifyPopulatedRollbackAsync(LabDbContext db)
    {
        var series = await AddSeriesAsync(db);
        series.AddMeasurement(Guid.NewGuid(), 20, "°C", DateTime.UtcNow);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(db.Database.GetMigrations().First());
        await migrator.MigrateAsync();
        Assert.Equal("Майданчик", (await db.Sites.AsNoTracking().SingleAsync()).Name);
        Assert.Empty(await db.Set<Experiment>().ToArrayAsync());
        Assert.Empty(await db.Set<Measurement>().ToArrayAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (postgresDatabase is not null)
        {
            try
            {
                await postgresDatabase.Database.EnsureDeletedAsync();
            }
            finally
            {
                await postgresDatabase.DisposeAsync();
            }
        }
    }

    private async Task<LabDbContext> CreatePostgresAsync(bool migrate = true)
    {
        var connection = new NpgsqlConnectionStringBuilder(PostgresTestDatabase.ConnectionString)
        {
            Database = "sigma_domain_" + Guid.NewGuid().ToString("N"),
        };
        var db = new LabDbContext(new DbContextOptionsBuilder<LabDbContext>()
            .UseNpgsql(connection.ConnectionString, pg => pg.MigrationsAssembly("Lab.Migrations.Postgres")).Options);
        postgresDatabase = db;
        if (migrate)
        {
            await db.Database.MigrateAsync();
        }

        return db;
    }

    private static async Task<Experiment> AddSeriesAsync(LabDbContext db)
    {
        var owner = new User("Дослідник", "owner@example.org");
        db.Add(owner);
        await db.SaveChangesAsync();
        var site = new Site { Name = "Майданчик", OwnerId = owner.Id };
        db.Add(site);
        await db.SaveChangesAsync();
        var series = new Experiment(site.Id, owner.Id, "Серія",
            new Instrument(owner.Id, "Прилад", "SN-1", "°C", -10, 50, 0.1),
            new ReferenceValue("Еталон", "°C", 20, 0.2));
        db.Add(series);
        await db.SaveChangesAsync();
        return series;
    }

    private static async Task VerifyLegacyUpgradeAsync(LabDbContext db)
    {
        var initial = db.Database.GetMigrations().First();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(initial);
        var builder = new DbContextOptionsBuilder<LegacyDbContext>();
        if (db.Database.IsSqlite())
        {
            builder.UseSqlite(db.Database.GetDbConnection());
        }
        else
        {
            builder.UseNpgsql(db.Database.GetDbConnection());
        }

        await using var legacy = new LegacyDbContext(builder.Options);
        legacy.Add(new LegacySite { Name = "Старі дані", OwnerId = 42, CreatedAt = DateTime.UtcNow });
        await legacy.SaveChangesAsync();
        await migrator.MigrateAsync();
        var stored = await db.Sites.AsNoTracking().SingleAsync();
        Assert.Equal("Старі дані", stored.Name);
        Assert.Equal(42, stored.LegacyOwnerId);
        Assert.Null(stored.OwnerId);
        Assert.Empty(await db.Set<User>().ToListAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        await migrator.MigrateAsync(initial);
        legacy.ChangeTracker.Clear();
        Assert.Equal(42, (await legacy.Set<LegacySite>().SingleAsync()).OwnerId);
        await migrator.MigrateAsync();
        Assert.Equal(42, (await db.Sites.AsNoTracking().SingleAsync()).LegacyOwnerId);
    }

    private sealed class LegacyDbContext(DbContextOptions<LegacyDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<LegacySite>().ToTable("sites");
        }
    }

    private sealed class LegacySite
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int OwnerId { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
