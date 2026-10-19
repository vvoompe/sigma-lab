using Lab.Application.Sites;
using Lab.Application.Abstractions;
using Lab.Application.Security;
using Lab.Domain.Users;
using Lab.Contracts.Sites;
using Lab.Domain.Sites;
using Lab.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Lab.Application.Tests;

/// <summary>
/// Тести сервісу на реальному DbContext, але на SQLite у пам'яті: швидко, без Docker і
/// без впливу на робочу базу. Схему тут створює EnsureCreated (у застосунку - міграції).
/// </summary>
public sealed class SiteServiceTests
{
    [Fact]
    public async Task Створення_майданчика_повертає_збережені_дані()
    {
        await using var db = await CreateContextAsync();
        var service = CreateService(db);

        var created = await service.CreateAsync(new CreateSiteRequest
        {
            Name = "Лісова ділянка",
            Latitude = 50.45,
            Longitude = 30.52,
        });

        Assert.True(created.Id > 0);
        Assert.Equal("Лісова ділянка", created.Name);
        Assert.Equal(50.45, created.Latitude);
        Assert.Equal(1, await db.Sites.Select(site => site.OwnerId).SingleAsync());

        var stored = await service.GetAsync(created.Id);
        Assert.NotNull(stored);
        Assert.Equal(created.Id, stored.Id);
    }

    [Fact]
    public async Task Список_повертається_відсортованим_за_назвою()
    {
        await using var db = await CreateContextAsync();
        var service = CreateService(db);

        await service.CreateAsync(new CreateSiteRequest { Name = "Явір", Latitude = 50.0, Longitude = 30.0 });
        await service.CreateAsync(new CreateSiteRequest { Name = "Береза", Latitude = 50.1, Longitude = 30.1 });

        var list = await service.ListAsync(new SiteQuery());

        Assert.Equal(["Береза", "Явір"], list.Items.Select(item => item.Name).ToArray());
    }

    [Fact]
    public async Task Неіснуючий_майданчик_повертає_null()
    {
        await using var db = await CreateContextAsync();
        var service = CreateService(db);

        Assert.Null(await service.GetAsync(9999));
    }

    [Theory]
    [InlineData("", 50.0, 30.0)]
    [InlineData("ab", 50.0, 30.0)]
    [InlineData("Нормальна назва", 91.0, 30.0)]
    [InlineData("Нормальна назва", 50.0, 181.0)]
    public async Task Некоректні_дані_відхиляються(string name, double latitude, double longitude)
    {
        await using var db = await CreateContextAsync();
        var service = CreateService(db);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.CreateAsync(new CreateSiteRequest
        {
            Name = name,
            Latitude = latitude,
            Longitude = longitude,
        }));
    }

    [Fact]
    public async Task Власник_може_оновити_майданчик()
    {
        await using var db = await CreateContextAsync();
        var service = CreateService(db);
        var created = await service.CreateAsync(new CreateSiteRequest
        {
            Name = "Початкова назва",
            Latitude = 50,
            Longitude = 30,
        });

        var status = await service.UpdateAsync(created.Id, new UpdateSiteRequest
        {
            Name = "Оновлена назва",
            Latitude = 51,
            Longitude = 31,
        });

        Assert.Equal(SiteUpdateStatus.Updated, status);
        var updated = await service.GetAsync(created.Id);
        Assert.NotNull(updated);
        Assert.Equal("Оновлена назва", updated.Name);
        Assert.Equal(51, updated.Latitude);
    }

    [Fact]
    public async Task Чужий_майданчик_прихований_для_читання_і_заборонений_для_зміни()
    {
        await using var db = await CreateContextAsync();
        var foreign = new Site { Name = "Чужий", OwnerId = 2 };
        db.Sites.Add(foreign);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        Assert.Null(await service.GetAsync(foreign.Id));
        Assert.Equal(
            SiteUpdateStatus.Forbidden,
            await service.UpdateAsync(foreign.Id, new UpdateSiteRequest
            {
                Name = "Зміна",
                Latitude = 50,
                Longitude = 30,
            }));
    }

    private static async Task<LabDbContext> CreateContextAsync()
    {
        // Одне з'єднання живе стільки ж, скільки контекст: у пам'яті SQLite база
        // існує лише поки відкрите з'єднання.
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<LabDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new LabDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Users.AddRange(
            new User("Власник", "owner@example.org"),
            new User("Інший власник", "other-owner@example.org"));
        await db.SaveChangesAsync();
        return db;
    }

    private static SiteService CreateService(LabDbContext db) => new(db, new TestCurrentUser(1));

    private sealed class TestCurrentUser(int id) : ICurrentUser
    {
        public int? Id { get; } = id;

        public string? Role => "Researcher";
    }
}
