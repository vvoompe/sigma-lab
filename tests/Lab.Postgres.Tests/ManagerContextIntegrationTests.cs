using System.Net;
using System.Text.Json;
using Lab.Application.Tests;
using Lab.Client.Maui.Data;
using Lab.Client.Maui.Services;
using Lab.Client.Maui.ViewModels;
using Lab.Postgres.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Lab.Postgres.Tests;

[Collection(PostgresTestGroup.Name)]
public sealed class ManagerContextIntegrationTests
{
    [Fact]
    public async Task Manager_CachesAssignedForeignSeries_ReadsOffline_AndCannotMutate() =>
        await VerifyAsync(null);

    [PostgresFact]
    public async Task Postgres_ManagerContext_PreservesOwnershipAndReference_InOfflineSqlite()
    {
        var connection = new NpgsqlConnectionStringBuilder(PostgresTestDatabase.ConnectionString)
        {
            Database = "resource_api_z4_context_" + Guid.NewGuid().ToString("N"),
        };
        await VerifyAsync(connection.ConnectionString);
    }

    private static async Task VerifyAsync(string? postgres)
    {
        var root = Path.Combine(Path.GetTempPath(), "sigma-manager-context-" + Guid.NewGuid().ToString("N"));
        var previous = Environment.GetEnvironmentVariable("LAB_DB_PROVIDER");
        Environment.SetEnvironmentVariable("LAB_DB_PROVIDER", "Sqlite");
        Directory.CreateDirectory(root);
        try
        {
            await using var fixture = new ResourceApiFixture(postgres);
            await fixture.InitializeAsync();
            var series = await fixture.CreateExperimentAsync(reference: true);
            await using (var db = fixture.CreateContext())
            {
                (await db.Sites.SingleAsync()).AssignManager(fixture.OtherId);
                await db.SaveChangesAsync();
            }
            fixture.Authenticate("LabManager", fixture.OtherId);
            var token = fixture.Client.DefaultRequestHeaders.Authorization!.Parameter!;
            var account = new ClientAccount("https://localhost/", fixture.OtherId, "Керівник", "manager@example.org",
                token, DateTime.UtcNow.AddHours(1), "LabManager");
            var api = new LabApiClient(fixture.Client, new Uri(account.Server));
            Assert.Empty(await api.SitesAsync());
            Assert.Empty(await api.InstrumentsAsync());
            Assert.Equal(series.Id, Assert.Single(await api.ExperimentsAsync()).Id);
            var context = await api.ExperimentContextAsync(series.Id);
            Assert.Equal(fixture.OwnerId, context.OwnerId);
            Assert.Equal(fixture.OwnerId, context.Site.OwnerId);
            Assert.Equal(fixture.OtherId, context.Site.ManagerId);
            Assert.Equal(fixture.ReferenceId, context.ReferenceValue!.Id);
            var model = new LaboratoryViewModel(fixture.Client, new Session(account), root);
            await model.InitializeAsync();
            await model.DownloadServerAsync();
            Assert.Equal(series.Id, Assert.Single(model.Experiments).Id);
            Assert.False(model.CanWrite);
            using var offline = new HttpClient(new Offline());
            var reopened = new LaboratoryViewModel(offline, new Session(account), root);
            await reopened.InitializeAsync();
            reopened.SelectedExperiment = Assert.Single(reopened.Experiments);
            reopened.MeasurementValue = "12";
            await reopened.AddOfflineMeasurementAsync();
            Assert.Empty(reopened.Operations);
            Assert.Contains("читання", reopened.Message);
            await reopened.CreateExperimentAsync();
            Assert.Contains("читання", reopened.Message);
            await reopened.SynchronizeAsync();
            Assert.Contains("читання", reopened.Message);
            await using (var services = account.OpenServices(root))
            await using (var scope = services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<Lab.Infrastructure.Persistence.LabDbContext>();
                var cached = await db.Experiments.SingleAsync();
                Assert.Equal(fixture.OwnerId, cached.OwnerId);
                Assert.Equal(fixture.ReferenceId, cached.ReferenceValueId);
                Assert.Equal(fixture.OtherId, (await db.Sites.SingleAsync()).ManagerId);
                Assert.Equal(fixture.OwnerId, (await db.Sites.SingleAsync()).OwnerId);
            }
            // Зміна ролі або призначення перевіряється сервером, не довірою до локального кешу.
            fixture.Authenticate("Researcher", fixture.OtherId);
            using var denied = await fixture.Client.GetAsync($"/api/experiments/{series.Id}/context");
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            fixture.Authenticate("LabManager", fixture.OtherId);
            await using (var db = fixture.CreateContext())
            {
                (await db.Sites.SingleAsync()).AssignManager(fixture.OwnerId);
                await db.SaveChangesAsync();
            }
            using var revoked = await fixture.Client.GetAsync($"/api/experiments/{series.Id}/context");
            Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
            fixture.Client.DefaultRequestHeaders.Authorization = null;
            using var anonymous = await fixture.Client.GetAsync($"/api/experiments/{series.Id}/context");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            fixture.Authenticate();
            using var schema = JsonDocument.Parse(await fixture.Client.GetStringAsync("/openapi/v1.json"));
            Assert.True(schema.RootElement.GetProperty("paths").GetProperty("/api/experiments/{id}/context")
                .GetProperty("get").GetProperty("responses").TryGetProperty("200", out _));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LAB_DB_PROVIDER", previous);
            Directory.Delete(root, true);
        }
    }

    private sealed class Session(ClientAccount account) : IClientSessionStore
    {
        public Task<ClientAccount?> LoadAsync() => Task.FromResult<ClientAccount?>(account);
        public Task SaveAsync(ClientAccount next) { account = next; return Task.CompletedTask; }
        public void Clear() { }
    }

    private sealed class Offline : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Офлайн; мережевих запитів бути не повинно.");
    }
}
