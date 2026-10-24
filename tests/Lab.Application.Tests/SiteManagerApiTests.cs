using System.Net;
using System.Net.Http.Json;
using Lab.Contracts.Sites;
using Lab.Domain.Security;
using Lab.Domain.Sites;
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

/// <summary>
/// Призначення керівника лабораторії на майданчик: доступ, роль, аудит і політика видалення.
/// </summary>
public sealed class SiteManagerApiTests
{
    [Fact]
    public async Task Адміністратор_призначає_менеджера_і_пише_аудит()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var factory = new ManagerApiFactory(connection);
        var seed = await InitializeAsync(factory);
        using var client = factory.CreateClient();
        ApiTestJwt.Authenticate(client, role: "Administrator", userId: seed.AdminId);

        using var response = await client.PutAsJsonAsync(
            $"/api/sites/{seed.SiteId}/manager", new AssignSiteManagerRequest(seed.ManagerId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
        var site = await db.Sites.SingleAsync(item => item.Id == seed.SiteId);
        Assert.Equal(seed.ManagerId, site.ManagerId);
        var audit = await db.AuditEntries.SingleAsync(item => item.TargetType == "Site" && item.TargetId == seed.SiteId);
        Assert.Equal("site.manager_assigned", audit.Action);
        Assert.Equal(seed.AdminId, audit.ActorId);
        Assert.Contains(seed.ManagerId.ToString(System.Globalization.CultureInfo.InvariantCulture), audit.Details);
    }

    [Fact]
    public async Task Дослідник_не_може_призначати_менеджера()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var factory = new ManagerApiFactory(connection);
        var seed = await InitializeAsync(factory);
        using var client = factory.CreateClient();
        ApiTestJwt.Authenticate(client, role: "Researcher", userId: seed.ResearchId);

        using var response = await client.PutAsJsonAsync(
            $"/api/sites/{seed.SiteId}/manager", new AssignSiteManagerRequest(seed.ManagerId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
        Assert.Null((await db.Sites.SingleAsync(item => item.Id == seed.SiteId)).ManagerId);
        Assert.False(await db.AuditEntries.AnyAsync());
    }

    [Fact]
    public async Task Користувач_без_ролі_LabManager_не_призначається()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var factory = new ManagerApiFactory(connection);
        var seed = await InitializeAsync(factory);
        using var client = factory.CreateClient();
        ApiTestJwt.Authenticate(client, role: "Administrator", userId: seed.AdminId);

        using var response = await client.PutAsJsonAsync(
            $"/api/sites/{seed.SiteId}/manager", new AssignSiteManagerRequest(seed.ResearchId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Невідомий_майданчик_дає_404()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var factory = new ManagerApiFactory(connection);
        var seed = await InitializeAsync(factory);
        using var client = factory.CreateClient();
        ApiTestJwt.Authenticate(client, role: "Administrator", userId: seed.AdminId);

        using var response = await client.PutAsJsonAsync(
            "/api/sites/99999/manager", new AssignSiteManagerRequest(seed.ManagerId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Повторне_призначення_того_самого_менеджера_не_додає_аудит()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var factory = new ManagerApiFactory(connection);
        var seed = await InitializeAsync(factory);
        using var client = factory.CreateClient();
        ApiTestJwt.Authenticate(client, role: "Administrator", userId: seed.AdminId);
        var body = new AssignSiteManagerRequest(seed.ManagerId);

        using var first = await client.PutAsJsonAsync($"/api/sites/{seed.SiteId}/manager", body);
        using var second = await client.PutAsJsonAsync($"/api/sites/{seed.SiteId}/manager", body);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
        Assert.Equal(1, await db.AuditEntries.CountAsync(item => item.TargetType == "Site"));
    }

    [Fact]
    public async Task Зміна_менеджера_пишеться_окремим_аудитом()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var factory = new ManagerApiFactory(connection);
        var seed = await InitializeAsync(factory);
        using var client = factory.CreateClient();
        ApiTestJwt.Authenticate(client, role: "Administrator", userId: seed.AdminId);

        using var first = await client.PutAsJsonAsync(
            $"/api/sites/{seed.SiteId}/manager", new AssignSiteManagerRequest(seed.ManagerId));
        using var second = await client.PutAsJsonAsync(
            $"/api/sites/{seed.SiteId}/manager", new AssignSiteManagerRequest(seed.SecondManagerId));

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
        var actions = await db.AuditEntries
            .Where(item => item.TargetType == "Site")
            .OrderBy(item => item.Id)
            .Select(item => item.Action)
            .ToListAsync();
        Assert.Equal(["site.manager_assigned", "site.manager_changed"], actions);
        Assert.Equal(seed.SecondManagerId, (await db.Sites.SingleAsync(item => item.Id == seed.SiteId)).ManagerId);
    }

    [Fact]
    public async Task Модель_забороняє_видалення_закріпленого_менеджера()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var factory = new ManagerApiFactory(connection);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();

        var foreignKey = db.Model.FindEntityType(typeof(Site))!
            .GetForeignKeys()
            .Single(item => item.Properties.Any(property => property.Name == nameof(Site.ManagerId)));

        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }

    private static async Task<SeedIds> InitializeAsync(ManagerApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
        await db.Database.EnsureCreatedAsync();

        var admin = new User("Адміністратор", "manager-admin@example.org");
        var manager = new User("Керівник", "manager-one@example.org");
        var secondManager = new User("Керівник другий", "manager-two@example.org");
        var researcher = new User("Дослідник", "manager-researcher@example.org");
        db.Users.AddRange(admin, manager, secondManager, researcher);

        db.UserCredentials.AddRange(
            new UserCredential(admin, "hash", DateTime.UtcNow, UserRole.Administrator),
            new UserCredential(manager, "hash", DateTime.UtcNow, UserRole.LabManager),
            new UserCredential(secondManager, "hash", DateTime.UtcNow, UserRole.LabManager),
            new UserCredential(researcher, "hash", DateTime.UtcNow, UserRole.Researcher));
        await db.SaveChangesAsync();

        var site = new Site
        {
            Name = "Майданчик менеджера",
            Latitude = 50.45,
            Longitude = 30.52,
            OwnerId = researcher.Id,
            CreatedAt = DateTime.UtcNow,
        };
        db.Sites.Add(site);
        await db.SaveChangesAsync();

        return new SeedIds(admin.Id, manager.Id, secondManager.Id, researcher.Id, site.Id);
    }

    private sealed record SeedIds(int AdminId, int ManagerId, int SecondManagerId, int ResearchId, int SiteId);

    private sealed class ManagerApiFactory(SqliteConnection connection) : WebApplicationFactory<Program>
    {
        static ManagerApiFactory() => ApiTestJwt.EnsureConfigured();

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