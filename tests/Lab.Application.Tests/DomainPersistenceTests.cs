using Lab.Domain.Experiments;
using Lab.Domain.Instruments;
using Lab.Domain.ReferenceValues;
using Lab.Domain.Sites;
using Lab.Domain.Users;
using Lab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lab.Application.Tests;

public sealed class DomainPersistenceTests
{
    [Fact]
    public async Task Модель_зберігає_серію_виміри_FK_та_UTC()
    {
        await using var db = await CreateDatabaseAsync();
        var series = await CreateSeriesAsync(db);
        var time = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
        series.AddMeasurement(Guid.NewGuid(), 20, "°C", time);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var stored = await db.Set<Experiment>().Include(item => item.Instrument).Include(item => item.Measurements).SingleAsync();
        Assert.Equal("°C", stored.Unit);
        Assert.Equal(time, Assert.Single(stored.Measurements).MeasuredAt);
        Assert.Equal(DateTimeKind.Utc, Assert.Single(stored.Measurements).MeasuredAt.Kind);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Видалення_виміру_чернетки_зберігається()
    {
        await using var db = await CreateDatabaseAsync();
        var series = await CreateSeriesAsync(db);
        var measurement = series.AddMeasurement(Guid.NewGuid(), 20, "°C", DateTime.UtcNow);
        await db.SaveChangesAsync();
        series.RemoveMeasurement(measurement);
        await db.SaveChangesAsync();
        Assert.Empty(await db.Set<Lab.Domain.Measurements.Measurement>().ToArrayAsync());
    }

    [Fact]
    public async Task Email_унікальний_без_урахування_регістру()
    {
        await using var db = await CreateDatabaseAsync();
        db.AddRange(new User("Перший", "Test@example.org"), new User("Другий", "test@EXAMPLE.org"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Серійний_номер_унікальний()
    {
        await using var db = await CreateDatabaseAsync();
        var series = await CreateSeriesAsync(db);
        db.Add(new Instrument(series.OwnerId, "Інший", "SN-1", "°C", -10, 50, 0.1));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ClientId_унікальний_між_різними_серіями()
    {
        await using var db = await CreateDatabaseAsync();
        var first = await CreateSeriesAsync(db);
        var second = new Experiment(first.SiteId, first.OwnerId, "Друга", first.Instrument ?? throw new InvalidOperationException());
        db.Add(second);
        var id = Guid.NewGuid();
        first.AddMeasurement(id, 20, "°C", DateTime.UtcNow);
        second.AddMeasurement(id, 21, "°C", DateTime.UtcNow);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Власник_майданчика_має_існувати()
    {
        await using var db = await CreateDatabaseAsync();
        db.Sites.Add(new Site { Name = "Сирота", OwnerId = 999 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Паралельний_підпис_не_дозволяє_додати_вимір_зі_старої_версії()
    {
        await using var db = await CreateDatabaseAsync();
        var series = await CreateSeriesAsync(db);
        series.AddMeasurement(Guid.NewGuid(), 20, "°C", DateTime.UtcNow);
        await db.SaveChangesAsync();
        await using var second = new LabDbContext(new DbContextOptionsBuilder<LabDbContext>()
            .UseSqlite(db.Database.GetDbConnection(), sqlite => sqlite.MigrationsAssembly("Lab.Migrations.Sqlite")).Options);
        var stale = await second.Set<Experiment>().Include(item => item.Instrument).Include(item => item.Measurements).SingleAsync();
        series.Sign(series.OwnerId, DateTime.UtcNow);
        await db.SaveChangesAsync();
        stale.AddMeasurement(Guid.NewGuid(), 21, "°C", DateTime.UtcNow);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        Assert.Equal(1, await db.Set<Lab.Domain.Measurements.Measurement>().CountAsync());
    }

    [Fact]
    public async Task Дата_майданчика_залишається_UTC_після_читання()
    {
        await using var db = await CreateDatabaseAsync();
        var time = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
        db.Sites.Add(new Site { Name = "UTC", CreatedAt = time });
        await db.SaveChangesAsync();
        var stored = await db.Sites.AsNoTracking().SingleAsync();
        Assert.Equal(time, stored.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, stored.CreatedAt.Kind);
    }

    [Fact]
    public async Task Зміна_завантаженого_виміру_зберігає_обидві_версії()
    {
        await using var db = await CreateDatabaseAsync();
        var series = await CreateSeriesAsync(db);
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

    private static async Task<LabDbContext> CreateDatabaseAsync()
    {
        var db = new LabDbContext(new DbContextOptionsBuilder<LabDbContext>()
            .UseSqlite("Data Source=:memory:", sqlite => sqlite.MigrationsAssembly("Lab.Migrations.Sqlite")).Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.MigrateAsync();
        return db;
    }

    private static async Task<Experiment> CreateSeriesAsync(LabDbContext db)
    {
        var user = new User("Дослідник", "test@example.org");
        db.Add(user);
        await db.SaveChangesAsync();
        var site = new Site { Name = "Майданчик", OwnerId = user.Id };
        db.Add(site);
        await db.SaveChangesAsync();
        var instrument = new Instrument(user.Id, "Прилад", "SN-1", "°C", -10, 50, 0.1);
        var reference = new ReferenceValue("Еталон", "°C", 20, 0.2);
        var series = new Experiment(site.Id, user.Id, "Серія", instrument, reference);
        db.Add(series);
        await db.SaveChangesAsync();
        return series;
    }
}
