using Lab.Application.Tests;
using Lab.Client.Maui.Data;
using Lab.Client.Maui.Services;
using Lab.Client.Maui.Sync;
using Lab.Client.Maui.ViewModels;
using Lab.Contracts.Experiments;
using Lab.Contracts.Instruments;
using Lab.Contracts.Sites;
using Lab.Postgres.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lab.Postgres.Tests;

[Collection(PostgresTestGroup.Name)]
public sealed class ClientApiIntegrationTests
{
    [PostgresFact]
    public async Task LostResponseOnRealPostgres_RetryMatchesTimestampPrecision_WithoutDuplicate()
    {
        var connection = new NpgsqlConnectionStringBuilder(PostgresTestDatabase.ConnectionString)
        {
            Database = "resource_api_z4_" + Guid.NewGuid().ToString("N"),
        };
        await using var fixture = new ResourceApiFixture(connection.ConnectionString);
        await fixture.InitializeAsync();
        var series = await fixture.CreateExperimentAsync();
        var path = Path.Combine(Path.GetTempPath(), "sigma-pg-retry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            using var http = new HttpClient(new LoseFirstPostResponse(fixture.Client));
            var queue = new SyncQueueStore(Path.Combine(path, "queue.json"));
            var item = new PendingMeasurement(series.Id, Guid.NewGuid(), 12, new DateTime(638974000000000007, DateTimeKind.Utc), series.Version);
            await MeasurementSynchronizer.EnqueueAsync(queue, item);
            var sync = new MeasurementSynchronizer(queue, new LabApiClient(http, new Uri("https://localhost/")));
            Assert.Equal(1, (await sync.SynchronizeAsync()).Failed);
            await queue.RetryAsync(item.ClientId);
            Assert.Equal(1, (await sync.SynchronizeAsync()).Sent);
            await using var db = fixture.CreateContext();
            Assert.Equal(1, await db.Measurements.CountAsync());
        }
        finally { Directory.Delete(path, true); }
    }

    [Fact]
    public async Task ViewModel_RestoresOfflineSeries_QueuesMeasurement_AndClearsScreenOnLogout()
    {
        var root = Path.Combine(Path.GetTempPath(), "sigma-viewmodel-" + Guid.NewGuid().ToString("N"));
        var previousProvider = Environment.GetEnvironmentVariable("LAB_DB_PROVIDER");
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("LAB_DB_PROVIDER", "Sqlite");
        try
        {
            var account = new ClientAccount("https://lab.example/", 1, "Юра", "yura@example.org", "expired", DateTime.UtcNow.AddHours(-1));
            var cache = new ExperimentCache(account, root);
            await cache.ReplaceAsync([new SiteDto(1, "Майданчик", 50, 30, DateTime.UtcNow)],
                [new InstrumentDto(1, 1, "Прилад", "SN", "m", 0, 100, 0.1)],
                [new ExperimentDto(1, "Серія", 1, 1, null, "Draft", DateTime.UtcNow, null, null, 3, "m")]);
            using var http = new HttpClient(new OfflineHandler());
            var sessions = new MemorySession(account);
            var model = new LaboratoryViewModel(http, sessions, root);
            await model.InitializeAsync();
            model.SelectedExperiment = Assert.Single(model.Experiments);
            model.MeasurementValue = "12.5";
            await model.AddOfflineMeasurementAsync();
            Assert.Equal(SyncQueueStatus.Pending, Assert.Single(model.Operations).Status);
            await model.SynchronizeAsync();
            Assert.Contains("Токен сплив", model.Message);
            var reopened = new LaboratoryViewModel(http, sessions, root);
            await reopened.InitializeAsync();
            Assert.Single(reopened.Experiments);
            Assert.Single(reopened.Operations);
            await reopened.LogoutAsync();
            Assert.Empty(reopened.Experiments);
            Assert.Empty(reopened.Operations);
            Assert.Null(await sessions.LoadAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable("LAB_DB_PROVIDER", previousProvider);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Client_RealApi_CachesSeries_SendsFiftyOfflineMeasurements_AndReadsStatistics()
    {
        var root = Path.Combine(Path.GetTempPath(), "sigma-client-api-" + Guid.NewGuid().ToString("N"));
        var previousProvider = Environment.GetEnvironmentVariable("LAB_DB_PROVIDER");
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("LAB_DB_PROVIDER", "Sqlite");
        try
        {
            await using var fixture = new ResourceApiFixture();
            await fixture.InitializeAsync();
            var series = await fixture.CreateExperimentAsync();
            var api = new LabApiClient(fixture.Client, new Uri("https://localhost/"));
            var account = new ClientAccount("https://localhost/", fixture.OwnerId, "Юра", "yura@example.org", "token", DateTime.UtcNow.AddHours(1));
            var cache = new ExperimentCache(account, root);
            await cache.ReplaceAsync(await api.SitesAsync(), await api.InstrumentsAsync(), await api.ExperimentsAsync());
            Assert.Equal(series.Id, Assert.Single(await cache.ListAsync()).Id);
            var queue = new SyncQueueStore(Path.Combine(root, "queue.json"));
            for (var index = 0; index < 50; index++)
            {
                await cache.ValidateMeasurementAsync(series.Id, index);
                await MeasurementSynchronizer.EnqueueAsync(queue,
                    new PendingMeasurement(series.Id, Guid.NewGuid(), index, DateTime.UtcNow, series.Version + index));
            }
            var synchronizer = new MeasurementSynchronizer(queue, api);
            Assert.Equal(50, (await synchronizer.SynchronizeAsync()).Sent);
            Assert.Equal(50, (await synchronizer.SynchronizeAsync()).Sent);
            var measurements = await api.MeasurementsAsync(series.Id);
            Assert.Equal(50, measurements.Count);
            Assert.Equal(50, measurements.Select(item => item.ClientId).Distinct().Count());
            var statistics = await api.StatisticsAsync(series.Id);
            Assert.Equal(50, statistics.Count);
            Assert.Equal(24.5, statistics.Mean);
            await using var db = fixture.CreateContext();
            Assert.Equal(50, await db.Measurements.CountAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable("LAB_DB_PROVIDER", previousProvider);
            Directory.Delete(root, true);
        }
    }

    private sealed class MemorySession(ClientAccount? account) : IClientSessionStore
    {
        public Task<ClientAccount?> LoadAsync() => Task.FromResult(account);
        public Task SaveAsync(ClientAccount next) { account = next; return Task.CompletedTask; }
        public void Clear() => account = null;
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Офлайн: жодного мережевого запиту не очікується.");
    }

    private sealed class LoseFirstPostResponse(HttpClient inner) : HttpMessageHandler
    {
        private bool lost;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // HttpRequestMessage не можна повторно передавати іншому HttpClient.
            using var forwarded = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers) forwarded.Headers.TryAddWithoutValidation(header.Key, header.Value);
            if (request.Content is not null)
            {
                forwarded.Content = new ByteArrayContent(await request.Content.ReadAsByteArrayAsync(cancellationToken));
                foreach (var header in request.Content.Headers) forwarded.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            var response = await inner.SendAsync(forwarded, cancellationToken);
            if (!lost && request.Method == HttpMethod.Post)
            {
                response.EnsureSuccessStatusCode();
                response.Dispose();
                lost = true;
                throw new HttpRequestException("Відповідь втрачена після збереження у PostgreSQL.");
            }
            return response;
        }
    }
}
