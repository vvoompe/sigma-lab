using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Lab.Contracts.Security;
using Lab.Domain.Security;
using Lab.Domain.Users;
using Lab.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lab.Application.Tests;

public sealed class AuthApiTests
{
    private const string Password = "Correct-Horse-123";

    [Fact]
    public async Task Реєстрація_зберігає_хеш_і_видає_дослідника_без_хешу_у_відповіді()
    {
        await using var connection = await OpenConnectionAsync();
        await using var factory = new AuthApiFactory(connection);
        using var client = factory.CreateClient();
        await InitializeDatabaseAsync(factory);

        using var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                Name = "Test User",
                Email = "user@example.org",
                Password,
                Role = "Administrator",
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var token = JsonSerializer.Deserialize<AccessTokenResponse>(content, JsonOptions);

        Assert.NotNull(token);
        Assert.Equal("Researcher", token.Role);
        Assert.NotEmpty(token.AccessToken);
        Assert.InRange(token.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(29), DateTime.UtcNow.AddMinutes(31));
        Assert.DoesNotContain("PasswordHash", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Password, content, StringComparison.Ordinal);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var authenticated = await client.GetAsync("/api/sites");
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
        var credential = await db.UserCredentials.Include(item => item.User).SingleAsync();
        Assert.NotNull(credential.User);
        Assert.Equal(credential.UserId, credential.User.Id);
        Assert.NotEqual(Password, credential.PasswordHash);
        Assert.Equal(UserRole.Researcher, credential.Role);
    }

    [Fact]
    public async Task Повторний_email_повертає_409_і_невалідний_пароль_400()
    {
        await using var connection = await OpenConnectionAsync();
        await using var factory = new AuthApiFactory(connection);
        using var client = factory.CreateClient();
        await InitializeDatabaseAsync(factory);

        using var created = await client.PostAsJsonAsync("/api/auth/register", NewRegistration());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var duplicate = await client.PostAsJsonAsync("/api/auth/register", NewRegistration("USER@example.org"));
        using var shortPassword = await client.PostAsJsonAsync(
            "/api/auth/register",
            NewRegistration("another@example.org", "short"));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, shortPassword.StatusCode);
    }

    [Fact]
    public async Task Правильний_пароль_видає_токен_а_невідомий_email_і_помилка_мають_однакову_відповідь()
    {
        await using var connection = await OpenConnectionAsync();
        await using var factory = new AuthApiFactory(connection);
        using var client = factory.CreateClient();
        await InitializeDatabaseAsync(factory);
        using var registered = await client.PostAsJsonAsync("/api/auth/register", NewRegistration());
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        using var accepted = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Email = "user@example.org", Password = Password });
        using var rejected = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Email = "user@example.org", Password = "Wrong-password-123" });
        using var unknown = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Email = "missing@example.org", Password = Password });

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var accessToken = await accepted.Content.ReadFromJsonAsync<AccessTokenResponse>();
        Assert.NotNull(accessToken);
        Assert.NotEmpty(accessToken.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await rejected.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Пять_помилок_блокують_вхід_на_15_хвилин()
    {
        await using var connection = await OpenConnectionAsync();
        await using var factory = new AuthApiFactory(connection);
        using var client = factory.CreateClient();
        await InitializeDatabaseAsync(factory);
        using var registered = await client.PostAsJsonAsync("/api/auth/register", NewRegistration());
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var response = await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest { Email = "user@example.org", Password = "Wrong-password-123" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var blocked = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Email = "user@example.org", Password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var credential = await scope.ServiceProvider.GetRequiredService<LabDbContext>().UserCredentials.SingleAsync();
        Assert.Equal(5, credential.FailedAttempts);
        Assert.True(credential.LockoutUntil > DateTime.UtcNow.AddMinutes(14));
    }

    [Fact]
    public async Task Адміністратор_змінює_роль_з_аудитом_але_не_власну()
    {
        await using var connection = await OpenConnectionAsync();
        await using var factory = new AuthApiFactory(connection);
        using var client = factory.CreateClient();
        await InitializeDatabaseAsync(factory);
        await SeedAdministratorAsync(factory);

        using var registered = await client.PostAsJsonAsync("/api/auth/register", NewRegistration());
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
        var targetId = await db.Users
            .Where(user => user.NormalizedEmail == "USER@EXAMPLE.ORG")
            .Select(user => user.Id)
            .SingleAsync();
        ApiTestJwt.Authenticate(client, role: "Administrator", userId: 1);

        using var changed = await client.PatchAsJsonAsync(
            $"/api/admin/users/{targetId}/role",
            new ChangeUserRoleRequest { Role = "LabManager" });
        using var selfChange = await client.PatchAsJsonAsync(
            "/api/admin/users/1/role",
            new ChangeUserRoleRequest { Role = "Researcher" });

        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, selfChange.StatusCode);
        db.ChangeTracker.Clear();
        var credential = await db.UserCredentials.SingleAsync(item => item.UserId == targetId);
        Assert.Equal(UserRole.LabManager, credential.Role);
        var audit = await db.AuditEntries.SingleAsync();
        Assert.Equal(1, audit.ActorId);
        Assert.Equal(targetId, audit.TargetId);
        Assert.Equal("Researcher -> LabManager", audit.Details);
    }

    [Fact]
    public async Task OpenAPI_позначає_реєстрацію_та_вхід_як_анонімні()
    {
        await using var connection = await OpenConnectionAsync();
        await using var factory = new AuthApiFactory(connection);
        using var client = factory.CreateClient();
        ApiTestJwt.Authenticate(client);

        using var document = await client.GetFromJsonAsync<JsonDocument>("/openapi/v1.json");
        Assert.NotNull(document);
        var paths = document.RootElement.GetProperty("paths");
        foreach (var path in new[] { "/api/auth/register", "/api/auth/login" })
        {
            var security = paths.GetProperty(path).GetProperty("post").GetProperty("security");
            Assert.Empty(security.EnumerateArray());
        }
    }

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    private static RegisterUserRequest NewRegistration(
        string email = "user@example.org",
        string password = Password) =>
        new()
        {
            Name = "Test User",
            Email = email,
            Password = password,
        };

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task InitializeDatabaseAsync(AuthApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<LabDbContext>().Database.EnsureCreatedAsync();
    }

    private static async Task SeedAdministratorAsync(AuthApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
        var user = new User("Administrator", "admin@example.org");
        db.UserCredentials.Add(new UserCredential(user, "test-hash", DateTime.UtcNow, UserRole.Administrator));
        await db.SaveChangesAsync();
    }

    private sealed class AuthApiFactory(SqliteConnection connection) : WebApplicationFactory<Program>
    {
        static AuthApiFactory() => ApiTestJwt.EnsureConfigured();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Database:MigrateOnStartup", "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<LabDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<LabDbContext>>();
                services.RemoveAll<LabDbContext>();
                services.AddDbContext<LabDbContext>(options => options.UseSqlite(connection));
            });
        }
    }
}
