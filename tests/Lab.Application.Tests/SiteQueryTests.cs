using Lab.Application.Sites;
using Lab.Application.Security;
using Lab.Contracts.Sites;
using Lab.Domain.Sites;
using Lab.Domain.Users;
using Lab.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Lab.Application.Tests;

public sealed class SiteQueryTests
{
    [Fact]
    public async Task Сторінки_не_дублюються_при_однакових_назвах()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateContextAsync(connection);
        db.Sites.AddRange(Enumerable.Range(1, 5).Select(_ => OwnedSite("Однакова назва")));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var first = await service.ListAsync(new SiteQuery { PageSize = 2 });
        var second = await service.ListAsync(new SiteQuery { Page = 2, PageSize = 2 });
        var last = await service.ListAsync(new SiteQuery { Page = 3, PageSize = 2 });

        Assert.Equal(5, first.TotalCount);
        Assert.Equal(5, second.TotalCount);
        Assert.Equal(2, second.Page);
        Assert.Equal(2, second.PageSize);
        Assert.Single(last.Items);
        Assert.Equal([1, 2, 3, 4, 5], first.Items.Concat(second.Items).Concat(last.Items).Select(site => site.Id));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(100)]
    [InlineData(101)]
    [InlineData(int.MaxValue)]
    public async Task Розмір_сторінки_обмежує_вибірку(int pageSize)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateContextAsync(connection);
        db.Sites.AddRange(Enumerable.Range(1, 101).Select(_ => OwnedSite("Майданчик")));
        await db.SaveChangesAsync();

        var result = await CreateService(db).ListAsync(new SiteQuery { PageSize = pageSize });

        Assert.Equal(101, result.TotalCount);
        Assert.Equal(Math.Min(pageSize, 100), result.Items.Count);
        Assert.Equal(Math.Min(pageSize, 100), result.PageSize);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public async Task Далека_сторінка_порожня_без_переповнення(int page)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateContextAsync(connection);
        db.Sites.Add(OwnedSite("Майданчик"));
        await db.SaveChangesAsync();

        var result = await CreateService(db).ListAsync(new SiteQuery { Page = page, PageSize = 100 });

        Assert.Empty(result.Items);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(page, result.Page);
    }

    [Fact]
    public async Task Фільтри_поєднуються_до_підрахунку_і_пагінації()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateContextAsync(connection);
        db.Sites.AddRange(
            OwnedSite("Ліс край", 50, 30),
            OwnedSite("Ліс центр", 51, 31),
            OwnedSite("Ліс далеко", 52, 31),
            OwnedSite("Ліс захід", 50, 29),
            OwnedSite("Ліс схід", 50, 32),
            OwnedSite("Ліс південь", 49, 30),
            OwnedSite("Поле", 50, 30));
        await db.SaveChangesAsync();

        var result = await CreateService(db).ListAsync(new SiteQuery
        {
            Name = "  Ліс  ", MinLatitude = 50, MaxLatitude = 51,
            MinLongitude = 30, MaxLongitude = 31, Page = 2, PageSize = 1,
        });

        Assert.Equal(2, result.TotalCount);
        Assert.Equal("Ліс центр", Assert.Single(result.Items).Name);
    }

    [Theory]
    [InlineData("%_", 1)]
    [InlineData("ЛІС", 0)]
    [InlineData("  ", 2)]
    [InlineData(null, 2)]
    public async Task Назва_шукається_буквально_з_урахуванням_регістру(string? name, int expected)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateContextAsync(connection);
        db.Sites.AddRange(OwnedSite("Ліс %_"), OwnedSite("Інший ліс"));
        await db.SaveChangesAsync();

        var result = await CreateService(db).ListAsync(new SiteQuery { Name = name });

        Assert.Equal(expected, result.TotalCount);
        Assert.Equal(expected, result.Items.Count);
    }

    [Fact]
    public async Task Порожня_база_повертає_параметри_за_замовчуванням()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateContextAsync(connection);

        var result = await CreateService(db).ListAsync(new SiteQuery());

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
    }

    [Theory]
    [MemberData(nameof(InvalidQueries))]
    public async Task Сервіс_також_відхиляє_некоректний_запит(SiteQuery query)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateContextAsync(connection);

        await Assert.ThrowsAsync<ArgumentException>(() => CreateService(db).ListAsync(query));
    }

    public static TheoryData<SiteQuery> InvalidQueries => new()
    {
        new SiteQuery { Page = 0 }, new SiteQuery { Page = -1 },
        new SiteQuery { PageSize = 0 }, new SiteQuery { PageSize = -1 },
        new SiteQuery { Name = new string('a', 101) },
        new SiteQuery { MinLatitude = -91 }, new SiteQuery { MaxLatitude = 91 },
        new SiteQuery { MinLongitude = -181 }, new SiteQuery { MaxLongitude = 181 },
        new SiteQuery { MinLatitude = double.NaN }, new SiteQuery { MaxLongitude = double.PositiveInfinity },
        new SiteQuery { MinLatitude = 50, MaxLatitude = 49 },
        new SiteQuery { MinLongitude = 31, MaxLongitude = 30 },
    };

    private static async Task<LabDbContext> CreateContextAsync(SqliteConnection connection)
    {
        var db = new LabDbContext(new DbContextOptionsBuilder<LabDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new User("Власник", "owner@example.org"));
        await db.SaveChangesAsync();
        return db;
    }

    private static SiteService CreateService(LabDbContext db) => new(db, new TestCurrentUser(1));

    private static Site OwnedSite(string name, double latitude = 0, double longitude = 0) =>
        new()
        {
            Name = name,
            Latitude = latitude,
            Longitude = longitude,
            OwnerId = 1,
        };

    private sealed class TestCurrentUser(int id) : ICurrentUser
    {
        public int? Id { get; } = id;

        public string? Role => "Researcher";
    }
}
