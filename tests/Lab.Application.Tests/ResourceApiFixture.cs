using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Lab.Application.Abstractions;
using Lab.Application.Resources;
using Lab.Application.Security;
using Lab.Contracts;
using Lab.Contracts.Experiments;
using Lab.Contracts.Instruments;
using Lab.Domain.Experiments;
using Lab.Domain.Instruments;
using Lab.Domain.ReferenceValues;
using Lab.Domain.Sites;
using Lab.Domain.Users;
using Lab.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lab.Application.Tests;

internal sealed class BeforeResourceSave : SaveChangesInterceptor
{
    internal Func<Task>? Callback { get; set; }
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var callback = Callback;
        Callback = null;
        if (callback is not null) await callback();
        return result;
    }
}

internal sealed class ResourceApiFixture(string? postgresConnection = null) : IAsyncDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private WebApplicationFactory<Program>? factory;
    private HttpClient? client;
    private bool ownsPostgresDatabase;
    internal HttpClient Client => client ?? throw new InvalidOperationException("Фабрику не ініціалізовано.");
    internal BeforeResourceSave BeforeSave { get; } = new();
    internal int OwnerId { get; private set; }
    internal int OtherId { get; private set; }
    internal int SiteId { get; private set; }
    internal int InstrumentId { get; private set; }
    internal int ReferenceId { get; private set; }

    private void ConfigureOptions(DbContextOptionsBuilder options)
    {
        if (postgresConnection is null)
            options.UseSqlite(connection, sqlite => sqlite.MigrationsAssembly("Lab.Migrations.Sqlite"));
        else
            options.UseNpgsql(postgresConnection, postgres => postgres.MigrationsAssembly("Lab.Migrations.Postgres"));
    }

    internal LabDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LabDbContext>();
        ConfigureOptions(options);
        return new LabDbContext(options.Options);
    }

    internal async Task InitializeAsync()
    {
        ApiTestJwt.EnsureConfigured();
        if (postgresConnection is null) await connection.OpenAsync();
        else
        {
            if (new Npgsql.NpgsqlConnectionStringBuilder(postgresConnection).Database?.StartsWith("resource_api_", StringComparison.Ordinal) != true)
                throw new InvalidOperationException("Потрібна окрема resource_api_ база тесту.");
            ownsPostgresDatabase = true;
        }
        factory = new ResourceFactory(ConfigureOptions, BeforeSave);
        client = factory.CreateClient();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        var owner = new User("Власник", "resource-owner@example.test");
        var other = new User("Інший", "resource-other@example.test");
        db.Users.AddRange(owner, other);
        await db.SaveChangesAsync();
        OwnerId = owner.Id;
        OtherId = other.Id;
        var site = new Site { Name = "Майданчик", Latitude = 50, Longitude = 30, OwnerId = owner.Id };
        var instrument = new Instrument(owner.Id, "Базовий", "Base-1", "m", 0, 100, 0.1);
        var reference = new ReferenceValue("Еталон", "m", 3, 1);
        db.Sites.Add(site);
        db.Instruments.Add(instrument);
        db.ReferenceValues.Add(reference);
        await db.SaveChangesAsync();
        SiteId = site.Id;
        InstrumentId = instrument.Id;
        ReferenceId = reference.Id;
        Authenticate();
    }

    internal void Authenticate(string role = "Researcher", int? userId = null) =>
        ApiTestJwt.Authenticate(Client, role: role, userId: userId ?? OwnerId);

    internal async Task<ExperimentDto> CreateExperimentAsync(bool reference = false)
    {
        using var response = await Client.PostAsJsonAsync("/api/experiments", new
        { name = "Серія", siteId = SiteId, instrumentId = InstrumentId, referenceValueId = reference ? (int?)ReferenceId : null });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ExperimentDto>();
        Assert.NotNull(result);
        Assert.Equal($"/api/experiments/{result.Id}", response.Headers.Location?.AbsolutePath);
        return result;
    }

    internal static AddMeasurementRequest Measurement(long version, double value) => new()
    { ClientId = Guid.NewGuid(), Value = value, MeasuredAt = DateTime.UtcNow, ExpectedVersion = version };

    internal async Task<MeasurementMutationDto> AddAsync(int id, long version, double value)
    {
        using var response = await Client.PostAsJsonAsync($"/api/experiments/{id}/measurements", Measurement(version, value));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<MeasurementMutationDto>();
        Assert.NotNull(result);
        return result;
    }

    internal async Task DisposeAsync()
    {
        client?.Dispose();
        if (factory is not null) await factory.DisposeAsync();
        if (ownsPostgresDatabase)
        {
            await using var db = CreateContext();
            await db.Database.EnsureDeletedAsync();
        }
        await connection.DisposeAsync();
    }

    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsync());

    private sealed class ResourceFactory(Action<DbContextOptionsBuilder> configure, BeforeResourceSave interceptor) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Database:MigrateOnStartup", "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<LabDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<LabDbContext>>();
                services.RemoveAll<LabDbContext>();
                services.AddDbContext<LabDbContext>(options =>
                {
                    configure(options);
                    options.AddInterceptors(interceptor);
                });
            });
        }
    }
}
