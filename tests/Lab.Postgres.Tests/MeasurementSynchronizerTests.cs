using System.Net;
using System.Net.Http.Json;
using Lab.Client.Maui.Services;
using Lab.Client.Maui.Sync;
using Lab.Contracts;
using Lab.Contracts.Experiments;

namespace Lab.Postgres.Tests;

public sealed class MeasurementSynchronizerTests
{
    [Fact]
    public async Task LocalUuidCannotBeReusedForDifferentData()
    {
        using var fixture = new Fixture();
        var item = Command(1);
        await MeasurementSynchronizer.EnqueueAsync(fixture.Queue, item);
        await Assert.ThrowsAsync<InvalidOperationException>(() => MeasurementSynchronizer.EnqueueAsync(fixture.Queue, item with { Value = 20 }));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Queue.EnqueueAsync("Measurement", "7", "{}", Guid.Empty));
        Assert.Single(await fixture.Queue.GetAllAsync());
    }

    [Fact]
    public async Task RetryAfterLostResponse_RecognizesCommittedUuid_WithoutSecondPost()
    {
        using var fixture = new Fixture();
        fixture.Server.LoseResponse = true;
        var command = Command(1);
        await MeasurementSynchronizer.EnqueueAsync(fixture.Queue, command);
        var first = await fixture.Synchronizer.SynchronizeAsync();
        Assert.Equal(1, first.Failed);
        await fixture.Queue.RetryAsync(command.ClientId);
        var retried = await fixture.Synchronizer.SynchronizeAsync();
        Assert.Equal(1, retried.Sent);
        Assert.Equal(1, fixture.Server.Posts);
        Assert.Single(fixture.Server.Measurements);
    }

    [Fact]
    public async Task ManyOfflineCommands_UseSequentialVersions_AndRepeatedSyncIsIdempotent()
    {
        using var fixture = new Fixture();
        for (var version = 1; version <= 50; version++)
            await MeasurementSynchronizer.EnqueueAsync(fixture.Queue, Command(version));
        Assert.Equal(50, (await fixture.Synchronizer.SynchronizeAsync()).Sent);
        Assert.Equal(50, (await fixture.Synchronizer.SynchronizeAsync()).Sent);
        Assert.Equal(50, fixture.Server.Posts);
        Assert.Equal(50, fixture.Server.Measurements.Count);
    }

    [Fact]
    public async Task VersionConflict_PreservesCommand_AndBlocksLaterCommandsOfSameSeries()
    {
        using var fixture = new Fixture();
        fixture.Server.Version = 8;
        await MeasurementSynchronizer.EnqueueAsync(fixture.Queue, Command(1));
        await MeasurementSynchronizer.EnqueueAsync(fixture.Queue, Command(2));
        var report = await fixture.Synchronizer.SynchronizeAsync();
        Assert.Equal(1, report.Failed);
        Assert.Equal(1, report.Pending);
        Assert.Equal(0, fixture.Server.Posts);
        Assert.Contains("Конфлікт", (await fixture.Queue.GetAllAsync())[0].LastError);
    }

    [Fact]
    public async Task DuplicateUuidWithDifferentPayload_IsConflict_NotSuccess()
    {
        using var fixture = new Fixture();
        var item = Command(1);
        fixture.Server.Measurements.Add(new(1, 7, item.ClientId, 999, item.MeasuredAt, 1, "°C"));
        await MeasurementSynchronizer.EnqueueAsync(fixture.Queue, item);
        Assert.Equal(1, (await fixture.Synchronizer.SynchronizeAsync()).Failed);
        Assert.Equal(0, fixture.Server.Posts);
    }

    [Fact]
    public async Task Cancellation_DoesNotMarkOperationSentOrFailed()
    {
        using var fixture = new Fixture();
        await MeasurementSynchronizer.EnqueueAsync(fixture.Queue, Command(1));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Synchronizer.SynchronizeAsync(cancellation.Token));
        Assert.Equal(SyncQueueStatus.Pending, Assert.Single(await fixture.Queue.GetAllAsync()).Status);
    }

    [Fact]
    public async Task Rebase_RequiresFailedState_AndExplicitPayloadChange()
    {
        using var fixture = new Fixture();
        var item = await MeasurementSynchronizer.EnqueueAsync(fixture.Queue, Command(1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Queue.RebaseAsync(item.OperationId, "{}"));
        await fixture.Queue.MarkFailedAsync(item.OperationId, "Конфлікт");
        var result = await fixture.Queue.RebaseAsync(item.OperationId, "{\"expectedVersion\":5}");
        Assert.Equal(SyncQueueStatus.Pending, result.Status);
        Assert.Contains("5", result.Payload);
        Assert.Null(result.LastError);
    }

    [Fact]
    public async Task Discard_IsExplicit_OnlyForFailedCommand()
    {
        using var fixture = new Fixture();
        var item = await MeasurementSynchronizer.EnqueueAsync(fixture.Queue, Command(1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Queue.DiscardFailedAsync(item.OperationId));
        await fixture.Queue.MarkFailedAsync(item.OperationId, "Підписана серія");
        await fixture.Queue.DiscardFailedAsync(item.OperationId);
        Assert.Empty(await fixture.Queue.GetAllAsync());
    }

    [Theory]
    [InlineData("http://lab.example")]
    [InlineData("https://user:password@lab.example")]
    [InlineData("https://lab.example/api")]
    public void InsecureOrAmbiguousServerAddress_IsRejected(string address) =>
        Assert.Throws<ArgumentException>(() => ClientAccount.ValidateServer(address));

    [Fact]
    public void Workspace_IsIsolatedByUserAndServer()
    {
        var first = new ClientAccount("https://one.example", 7, "Юра", "a@example.org", "token", DateTime.UtcNow);
        Assert.NotEqual(first.WorkspacePath("root"), (first with { UserId = 8 }).WorkspacePath("root"));
        Assert.NotEqual(first.WorkspacePath("root"), (first with { Server = "https://two.example" }).WorkspacePath("root"));
    }

    private static PendingMeasurement Command(long version) => new(7, Guid.NewGuid(), 12.5, DateTime.UtcNow, version);

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "sigma-sync-" + Guid.NewGuid().ToString("N"));
        private readonly HttpClient http;
        public Fixture()
        {
            Directory.CreateDirectory(directory);
            Queue = new(Path.Combine(directory, "queue.json"));
            http = new(Server);
            Synchronizer = new(Queue, new LabApiClient(http, new Uri("https://lab.example/"), "test-token"));
        }
        public FakeServer Server { get; } = new();
        public SyncQueueStore Queue { get; }
        public MeasurementSynchronizer Synchronizer { get; }
        public void Dispose() { http.Dispose(); Directory.Delete(directory, true); }
    }

    private sealed class FakeServer : HttpMessageHandler
    {
        public long Version { get; set; } = 1;
        public int Posts { get; private set; }
        public bool LoseResponse { get; set; }
        public List<MeasurementDto> Measurements { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith("/measurements", StringComparison.Ordinal))
                return Reply(new PagedResult<MeasurementDto>(Measurements.ToArray(), Measurements.Count, 1, 100));
            if (request.Method == HttpMethod.Get)
                return Reply(new ExperimentDto(7, "Серія", 1, 1, null, "Draft", DateTime.UtcNow, null, null, Version, "°C"));
            var item = await request.Content!.ReadFromJsonAsync<AddMeasurementRequest>(cancellationToken);
            Assert.Equal(Version, item!.ExpectedVersion);
            Posts++;
            Version++;
            var measurement = new MeasurementDto(Posts, 7, item.ClientId!.Value, item.Value!.Value, item.MeasuredAt!.Value, 1, "°C");
            Measurements.Add(measurement);
            if (LoseResponse) { LoseResponse = false; throw new HttpRequestException("Втрачено відповідь після commit."); }
            return Reply(new MeasurementMutationDto(measurement, Version));
        }
        private static HttpResponseMessage Reply<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
