using System.Net;
using System.Net.Http.Json;
using Lab.Contracts.ReferenceValues;
using Lab.Domain.Experiments;
using Lab.Domain.Instruments;
using Lab.Domain.ReferenceValues;
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

/// <summary>Еталони: читання всім автентифікованим, зміни лише адміністратору, захист від видалення використаного.</summary>
public sealed class ReferenceValueApiTests
{
    [Fact]
    public async Task Адміністратор_створює_оновлює_і_видаляє_еталон()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var factory = new ReferenceApiFactory(connection);
        await SeedAsync(factory);
        using var client = factory.CreateClient();
        var adminId = await AdminIdAsync(factory);
        ApiTestJwt.Authenticate(client, role: "Administrator", userId: adminId);

        using var created = await client.PostAsJsonAsync(
            "/api/reference-values", new { name = "Еталон pH", unit = "pH", value = 7.4, absoluteTolerance = 0.05 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = await created.Content.ReadFromJsonAsync<ReferenceValueDto>();
        Assert.NotNull(dto);
        Assert.Equal("Еталон pH", dto.Name);
        Assert.Equal(7.4, dto.Value);

        using var updated = await client.PutAsJsonAsync(
            $"/api/reference-values/{dto.Id}", new { name = "Еталон pH 7", unit = "pH", value = 7.0, absoluteTolerance = 0.1 });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(7.0, (await updated.Content.ReadFromJsonAsync<ReferenceValueDto>())!.Value);

        using var list = await client.GetAsync("/api/reference-values");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Single(await list.Content.ReadFromJsonAsync<List<ReferenceValueDto>>() ?? []);

        using var deleted = await client.DeleteAsync($"/api/reference-values/{dto.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Дослідник_не_може_змінювати_еталони()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var factory = new ReferenceApiFactory(connection);
        await SeedAsync(factory);
        using var client = factory.CreateClient();
        var researcherId = await ResearcherIdAsync(factory);
        ApiTestJwt.Authenticate(client, role: "Researcher", userId: researcherId);

        using var created = await client.PostAsJsonAsync(
            "/api/reference-values", new { name = "Еталон", unit = "C", value = 1, absoluteTolerance = 0.1 });

        Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);
    }

    [Fact]
    public async Task Читання_доступне_досліднику()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var factory = new ReferenceApiFactory(connection);
        await SeedAsync(factory);
        using var client = factory.CreateClient();
        var researcherId = await ResearcherIdAsync(factory);
        ApiTestJwt.Authenticate(client, role: "Researcher", userId: researcherId);

        using var list = await client.GetAsync("/api/reference-values");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [Fact]
    public async Task Відємний_допуск_відхиляється()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var factory = new ReferenceApiFactory(connection);
        await SeedAsync(factory);
        using var client = factory.CreateClient();
        var adminId = await AdminIdAsync(factory);
        ApiTestJwt.Authenticate(client, role: "Administrator", userId: adminId);

        using var created = await client.PostAsJsonAsync(
            "/api/reference-values", new { name = "Еталон", unit = "C", value = 1, absoluteTolerance = -0.1 });

        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
    }

    [Fact]
    public async Task Видалення_використаного_еталона_дає_409()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var factory = new ReferenceApiFactory(connection);
        var ids = await SeedAsync(factory);
        using var client = factory.CreateClient();
        ApiTestJwt.Authenticate(client, role: "Administrator", userId: ids.AdminId);

        using var created = await client.PostAsJsonAsync(
            "/api/reference-values", new { name = "Еталон", unit = "C", value = 12, absoluteTolerance = 0.5 });
        var dto = await created.Content.ReadFromJsonAsync<ReferenceValueDto>();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
            var instrument = new Instrument(ids.ResearcherId, "Термометр", "REF-1", "C", -10, 50, 0.1);
            db.Instruments.Add(instrument);
            var site = new Site { Name = "Майданчик", Latitude = 50, Longitude = 30, OwnerId = ids.ResearcherId };
            db.Sites.Add(site);
            await db.SaveChangesAsync();
            var reference = await db.ReferenceValues.SingleAsync(item => item.Id == dto!.Id);
            db.Experiments.Add(new Experiment(site.Id, ids.ResearcherId, "Серія", instrument, reference));
            await db.SaveChangesAsync();
        }

        using var deleted = await client.DeleteAsync($"/api/reference-values/{dto!.Id}");

        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
    }

    private sealed record Ids(int AdminId, int ResearcherId);

    private static async Task<Ids> SeedAsync(ReferenceApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
        await db.Database.EnsureCreatedAsync();
        if (await db.Users.AnyAsync())
        {
            var existing = await db.Users.OrderBy(item => item.Id).ToListAsync();
            return new Ids(existing[0].Id, existing[^1].Id);
        }

        var admin = new User("Адміністратор", "ref-admin@example.org");
        var researcher = new User("Дослідник", "ref-researcher@example.org");
        db.Users.AddRange(admin, researcher);
        db.UserCredentials.AddRange(
            new UserCredential(admin, "hash", DateTime.UtcNow, UserRole.Administrator),
            new UserCredential(researcher, "hash", DateTime.UtcNow, UserRole.Researcher));
        await db.SaveChangesAsync();
        return new Ids(admin.Id, researcher.Id);
    }

    private static async Task<int> AdminIdAsync(ReferenceApiFactory factory) => (await SeedAsync(factory)).AdminId;

    private static async Task<int> ResearcherIdAsync(ReferenceApiFactory factory) => (await SeedAsync(factory)).ResearcherId;

    private sealed class ReferenceApiFactory(SqliteConnection connection) : WebApplicationFactory<Program>
    {
        static ReferenceApiFactory() => ApiTestJwt.EnsureConfigured();

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
