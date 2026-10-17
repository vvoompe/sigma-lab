using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lab.Api.Security;
using Lab.Contracts;
using Lab.Contracts.Sites;
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

public sealed class SitesApiTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("короткий")]
    public void Ключ_JWT_без_мінімальної_довжини_зупиняє_конфігурацію(string? key)
    {
        Assert.Throws<InvalidOperationException>(() =>
            LabSecurityServiceCollectionExtensions.CreateSigningKey(key));
    }

    [Fact]
    public void Конфігурація_без_Jwt__Key_зупиняє_запуск_API()
    {
        var previousKey = Environment.GetEnvironmentVariable("Jwt__Key");
        try
        {
            Environment.SetEnvironmentVariable("Jwt__Key", null);
            Assert.Throws<InvalidOperationException>(() =>
                new ServiceCollection().AddLabSecurity());
        }
        finally
        {
            Environment.SetEnvironmentVariable("Jwt__Key", previousKey);
        }
    }

    [Fact]
    public async Task HTTP_створення_читання_і_фільтрована_сторінка()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var factory = new SitesApiFactory(connection);
        using var client = factory.CreateClient();
        ApiTestJwt.Authenticate(client);
        await InitializeDatabaseAsync(factory);

        for (var index = 0; index < 3; index++)
        {
            using var created = await client.PostAsJsonAsync("/api/sites", new CreateSiteRequest
            {
                Name = "Ліс " + index, Latitude = 50 + index, Longitude = 30,
            });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.NotNull(created.Headers.Location);
            using var stored = await client.GetAsync(created.Headers.Location);
            Assert.Equal(HttpStatusCode.OK, stored.StatusCode);
        }

        var url = "/api/sites?name=" + Uri.EscapeDataString("Ліс") + "&maxLatitude=51&page=2&pageSize=1";
        var page = await client.GetFromJsonAsync<PagedResult<SiteDto>>(url);

        Assert.NotNull(page);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.Equal(1, page.PageSize);
        Assert.Equal("Ліс 1", Assert.Single(page.Items).Name);

        using var missing = await client.GetAsync("/api/sites/9999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("pageSize=0")]
    [InlineData("page=abc")]
    [InlineData("page=2147483648")]
    [InlineData("minLatitude=-91")]
    [InlineData("maxLongitude=181")]
    [InlineData("minLatitude=NaN")]
    [InlineData("maxLongitude=Infinity")]
    [InlineData("minLatitude=51&maxLatitude=50")]
    [InlineData("minLongitude=31&maxLongitude=30")]
    public async Task HTTP_некоректні_фільтри_дають_400_ProblemDetails(string query)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var factory = new SitesApiFactory(connection);
        using var client = factory.CreateClient();
        ApiTestJwt.Authenticate(client);

        // Схему навмисно не створено: валідація має спрацювати до запиту до БД.
        using var response = await client.GetAsync("/api/sites?" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
        Assert.NotEmpty(problem.Errors);
    }

    [Fact]
    public async Task OpenAPI_описує_фільтри_сторінку_і_400()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var factory = new SitesApiFactory(connection);
        using var client = factory.CreateClient();
        ApiTestJwt.Authenticate(client);
        using var document = await client.GetFromJsonAsync<JsonDocument>("/openapi/v1.json");

        Assert.NotNull(document);
        var operation = document.RootElement.GetProperty("paths").GetProperty("/api/sites").GetProperty("get");
        var schemes = document.RootElement.GetProperty("components").GetProperty("securitySchemes");
        var bearer = schemes.GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());
        Assert.NotEmpty(document.RootElement.GetProperty("security").EnumerateArray());
        var names = operation.GetProperty("parameters").EnumerateArray()
            .Select(parameter => parameter.GetProperty("name").GetString()).ToArray();
        foreach (var expected in new[] { "Page", "PageSize", "Name", "MinLatitude", "MaxLatitude", "MinLongitude", "MaxLongitude" })
        {
            Assert.Contains(expected, names);
        }

        var responses = operation.GetProperty("responses");
        Assert.True(responses.TryGetProperty("400", out _));
        var schema = responses.GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        var schemaReference = schema.GetProperty("$ref").GetString();
        Assert.NotNull(schemaReference);
        var schemaName = schemaReference.Split('/')[^1];
        var properties = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty(schemaName).GetProperty("properties");
        foreach (var expected in new[] { "items", "totalCount", "page", "pageSize" })
        {
            Assert.True(properties.TryGetProperty(expected, out _));
        }
    }

    [Fact]
    public async Task HTTP_великий_розмір_обмежується_а_далека_сторінка_порожня()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var factory = new SitesApiFactory(connection);
        using var client = factory.CreateClient();
        ApiTestJwt.Authenticate(client);
        await InitializeDatabaseAsync(factory);

        var page = await client.GetFromJsonAsync<PagedResult<SiteDto>>("/api/sites?page=2147483647&pageSize=2147483647");

        Assert.NotNull(page);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(int.MaxValue, page.Page);
        Assert.Equal(100, page.PageSize);
    }

    private static async Task InitializeDatabaseAsync(SitesApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<LabDbContext>().Database.EnsureCreatedAsync();
    }

    [Fact]
    public async Task HTTP_без_токена_повертає_401()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var factory = new SitesApiFactory(connection);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/sites");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HTTP_токен_із_невалідним_підписом_повертає_401()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var factory = new SitesApiFactory(connection);
        using var client = factory.CreateClient();
        ApiTestJwt.Authenticate(client, signingKey: System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        using var response = await client.GetAsync("/api/sites");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HTTP_токен_із_невідомою_роллю_повертає_403()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var factory = new SitesApiFactory(connection);
        using var client = factory.CreateClient();
        ApiTestJwt.Authenticate(client, role: "Guest");

        using var response = await client.GetAsync("/api/sites");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed class SitesApiFactory(SqliteConnection connection) : WebApplicationFactory<Program>
    {
        static SitesApiFactory() => ApiTestJwt.EnsureConfigured();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Database:MigrateOnStartup", "false");
            builder.ConfigureServices(services =>
            {
                // Ізольована SQLite навіть коли серверний CI вибирає Postgres через середовище.
                services.RemoveAll<DbContextOptions<LabDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<LabDbContext>>();
                services.RemoveAll<LabDbContext>();
                services.AddDbContext<LabDbContext>(options => options.UseSqlite(connection));
            });
        }
    }
}
