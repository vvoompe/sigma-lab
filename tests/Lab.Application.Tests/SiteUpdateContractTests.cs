using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Lab.Application.Security;
using Lab.Application.Sites;
using Lab.Contracts.Sites;
using Lab.Domain.Sites;
using Lab.Domain.Users;
using Lab.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lab.Application.Tests;

/// <summary>HTTP-контракт повної заміни Site, включно з відсутніми JSON-полями.</summary>
public sealed class SiteUpdateContractTests : IAsyncLifetime, IAsyncDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private SiteApiFactory factory = null!;
    private HttpClient client = null!;
    private int siteId;
    private int otherUserId;
    private int ownerId;
    private DateTime createdAt;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        factory = new SiteApiFactory(connection);
        client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
        await db.Database.MigrateAsync();
        var owner = new User("Власник", "site-contract-owner@example.org");
        var other = new User("Інший", "site-contract-other@example.org");
        db.Users.AddRange(owner, other);
        await db.SaveChangesAsync();
        ownerId = owner.Id;
        otherUserId = other.Id;
        createdAt = new DateTime(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc);
        var site = new Site { Name = "Початкова назва", Latitude = 50, Longitude = 30,
            OwnerId = ownerId, CreatedAt = createdAt };
        db.Sites.Add(site);
        await db.SaveChangesAsync();
        siteId = site.Id;
        ApiTestJwt.Authenticate(client, userId: ownerId);
    }

    [Theory]
    [InlineData("{\"name\":\"Нова назва\"}")]
    [InlineData("{\"name\":\"Нова назва\",\"latitude\":51}")]
    [InlineData("{\"name\":\"Нова назва\",\"longitude\":31}")]
    [InlineData("{\"name\":\"Нова назва\",\"latitude\":null,\"longitude\":31}")]
    [InlineData("{\"name\":\"Нова назва\",\"latitude\":51,\"longitude\":null}")]
    [InlineData("{\"name\":\" а \",\"latitude\":51,\"longitude\":31}")]
    [InlineData("{\"name\":\"Нова назва\",\"latitude\":91,\"longitude\":31}")]
    public async Task Неповний_або_невалідний_PUT_дає_400_і_не_змінює_запис(string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await client.PutAsync($"/api/sites/{siteId}", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.NotEmpty(problem.Errors);
        await AssertOriginalSiteAsync();
    }

    [Fact]
    public async Task Явні_нульові_координати_дозволені_власник_і_дата_не_змінюються()
    {
        using var response = await client.PutAsJsonAsync($"/api/sites/{siteId}", new
        {
            Name = "  Нова назва  ", Latitude = 0, Longitude = 0,
            OwnerId = otherUserId, CreatedAt = DateTime.UtcNow,
        });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
        var stored = await db.Sites.AsNoTracking().SingleAsync();
        Assert.Equal("Нова назва", stored.Name);
        Assert.Equal(0, stored.Latitude);
        Assert.Equal(0, stored.Longitude);
        Assert.Equal(ownerId, stored.OwnerId);
        Assert.Equal(createdAt, stored.CreatedAt);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Сервіс_також_відхиляє_неповний_запит_без_MVC(bool hasLatitude, bool hasLongitude)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
        var service = new SiteService(db, new TestUser(ownerId));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(siteId, new UpdateSiteRequest
        {
            Name = "Нова назва", Latitude = hasLatitude ? 51 : null, Longitude = hasLongitude ? 31 : null,
        }));
        Assert.False(db.ChangeTracker.HasChanges());
        await AssertOriginalSiteAsync();
    }

    [Fact]
    public async Task Чужий_PUT_дає_403_ProblemDetails_без_зміни_даних()
    {
        ApiTestJwt.Authenticate(client, userId: otherUserId);
        using var response = await client.PutAsJsonAsync($"/api/sites/{siteId}", new
        {
            Name = "Чужа зміна", Latitude = 51, Longitude = 31,
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(403, problem.Status);
        await AssertOriginalSiteAsync();
    }

    [Fact]
    public async Task Відсутній_PUT_дає_404_а_без_токена_401()
    {
        var request = new UpdateSiteRequest { Name = "Нова назва", Latitude = 51, Longitude = 31 };
        using var missing = await client.PutAsJsonAsync("/api/sites/2147483647", request);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
        client.DefaultRequestHeaders.Authorization = null;
        using var anonymous = await client.PutAsJsonAsync($"/api/sites/{siteId}", request);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        await AssertOriginalSiteAsync();
    }

    [Fact]
    public async Task OpenAPI_PUT_вимагає_обидві_координати_і_назву()
    {
        using var document = await client.GetFromJsonAsync<JsonDocument>("/openapi/v1.json");
        Assert.NotNull(document);
        var operation = document.RootElement.GetProperty("paths").GetProperty("/api/sites/{id}").GetProperty("put");
        var schema = operation.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        var name = schema.GetProperty("$ref").GetString()!.Split('/')[^1];
        var required = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(name)
            .GetProperty("required").EnumerateArray().Select(item => item.GetString()).ToArray();
        foreach (var field in new[] { "name", "latitude", "longitude" })
        {
            Assert.Contains(field, required);
        }
    }

    private async Task AssertOriginalSiteAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
        var stored = await db.Sites.AsNoTracking().SingleAsync();
        Assert.Equal("Початкова назва", stored.Name);
        Assert.Equal(50, stored.Latitude);
        Assert.Equal(30, stored.Longitude);
        Assert.Equal(ownerId, stored.OwnerId);
        Assert.Equal(createdAt, stored.CreatedAt);
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await factory.DisposeAsync();
        await connection.DisposeAsync();
    }

    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsync());

    private sealed class TestUser(int id) : ICurrentUser
    {
        public int? Id => id;
        public string? Role => "Researcher";
    }

    private sealed class SiteApiFactory(SqliteConnection connection) : WebApplicationFactory<Program>
    {
        static SiteApiFactory() => ApiTestJwt.EnsureConfigured();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Database:MigrateOnStartup", "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<LabDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<LabDbContext>>();
                services.RemoveAll<LabDbContext>();
                services.AddDbContext<LabDbContext>(options => options.UseSqlite(connection,
                    sqlite => sqlite.MigrationsAssembly("Lab.Migrations.Sqlite")));
            });
        }
    }
}
