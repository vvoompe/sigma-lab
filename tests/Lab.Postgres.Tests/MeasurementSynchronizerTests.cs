using System.Net;
using System.Net.Http.Json;
using Lab.Client.Maui.Services;
using Lab.Client.Maui.Sync;
using Lab.Contracts;
using Lab.Contracts.Experiments;
using Lab.Contracts.Sync;

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
    public async Task RetryAfterLostResponse_RecognizesCommittedUuid_WithoutSecondInsert()
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
    public async Task FiftyOfflineCommands_UseOneHttp207Batch_AndRepeatedSyncIsIdempotent()
    {
        using var fixture = new Fixture();
        for (var version = 1; version <= 50; version++)
            await MeasurementSynchronizer.EnqueueAsync(fixture.Queue, Command(version));
        Assert.Equal(50, (await fixture.Synchronizer.SynchronizeAsync()).Sent);
        Assert.Equal(50, (await fixture.Synchronizer.SynchronizeAsync()).Sent);
        Assert.Equal(50, fixture.Server.Posts);
        Assert.Equal([50], fixture.Server.BatchSizes);
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
        Assert.Contains("409", (await fixture.Queue.GetAllAsync())[0].LastError);
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

    [Fact]
    public async Task PartialBatchFailure_DoesNotAcknowledgeFailedItem_AndOtherSeriesContinue()
    {
        using var fixture = new Fixture();
        var first = Command(1);
        var rejected = Command(1) with { ExperimentId = 8 };
        var third = Command(1) with { ExperimentId = 9 };
        fixture.Server.FailClientId = rejected.ClientId;
        foreach (var item in new[] { first, rejected, third }) await MeasurementSynchronizer.EnqueueAsync(fixture.Queue, item);
        var report = await fixture.Synchronizer.SynchronizeAsync();
        Assert.Equal(2, report.Sent);
        Assert.Equal(1, report.Failed);
        var failed = Assert.Single(await fixture.Queue.GetAllAsync(), item => item.Status == SyncQueueStatus.Failed);
        Assert.Equal(rejected.ClientId, failed.OperationId);
        Assert.Equal(2, fixture.Server.Measurements.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidBatchReport_NeverMarksCommandsSent(bool reorder)
    {
        using var fixture = new Fixture();
        fixture.Server.ReverseResults = reorder;
        fixture.Server.OmitLastResult = !reorder;
        await MeasurementSynchronizer.EnqueueAsync(fixture.Queue, Command(1));
        await MeasurementSynchronizer.EnqueueAsync(fixture.Queue, Command(2));
        var report = await fixture.Synchronizer.SynchronizeAsync();
        Assert.Equal(0, report.Sent);
        Assert.Equal(2, report.Failed);
        Assert.Equal(2, fixture.Server.Measurements.Count);
    }

    [Fact]
    public async Task SuccessfulHttpWithWrongMeasurementPayload_IsNotAcknowledged()
    {
        using var fixture = new Fixture();
        fixture.Server.WrongPayload = true;
        await MeasurementSynchronizer.EnqueueAsync(fixture.Queue, Command(1));
        Assert.Equal(0, (await fixture.Synchronizer.SynchronizeAsync()).Sent);
        Assert.Equal(SyncQueueStatus.Failed, Assert.Single(await fixture.Queue.GetAllAsync()).Status);
    }

    [Fact]
    public async Task LostWholeBatchResponse_RetryUsesSameUuidsWithoutDuplicates()
    {
        using var fixture = new Fixture();
        fixture.Server.LoseResponse = true;
        var commands = new[] { Command(1), Command(2) };
        foreach (var command in commands) await MeasurementSynchronizer.EnqueueAsync(fixture.Queue, command);
        Assert.Equal(2, (await fixture.Synchronizer.SynchronizeAsync()).Failed);
        foreach (var command in commands) await fixture.Queue.RetryAsync(command.ClientId);
        Assert.Equal(2, (await fixture.Synchronizer.SynchronizeAsync()).Sent);
        Assert.Equal(2, fixture.Server.Posts);
        Assert.Equal([2], fixture.Server.BatchSizes);
    }

    [Fact]
    public async Task RetryingFirstFailedCommand_IsNotBlockedByLaterFailedCommand()
    {
        using var fixture = new Fixture();
        fixture.Server.LoseResponse = true;
        var first = Command(1);
        var second = Command(2);
        await MeasurementSynchronizer.EnqueueAsync(fixture.Queue, first);
        await MeasurementSynchronizer.EnqueueAsync(fixture.Queue, second);
        Assert.Equal(2, (await fixture.Synchronizer.SynchronizeAsync()).Failed);
        await fixture.Queue.RetryAsync(first.ClientId);
        var retried = await fixture.Synchronizer.SynchronizeAsync();
        Assert.Equal(1, retried.Sent);
        Assert.Equal(1, retried.Failed);
        Assert.Equal(2, fixture.Server.Posts);
        Assert.Equal(SyncQueueStatus.Sent, (await fixture.Queue.GetAllAsync())[0].Status);
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
        private readonly Dictionary<int, long> versions = [];
        public long Version { get; set; } = 1;
        public int Posts { get; private set; }
        public bool LoseResponse { get; set; }
        public bool WrongPayload { get; set; }
        public bool ReverseResults { get; set; }
        public bool OmitLastResult { get; set; }
        public Guid? FailClientId { get; set; }
        public List<int> BatchSizes { get; } = [];
        public List<MeasurementDto> Measurements { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get)
            {
                var id = int.Parse(path.Split('/')[3], System.Globalization.CultureInfo.InvariantCulture);
                var version = id == 7 ? Version : versions.GetValueOrDefault(id, 1);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new ExperimentDto(id, "Серія", 1, 1, null, "Draft", DateTime.UtcNow, null, null, version, "°C")),
                };
            }
            Assert.Equal(HttpMethod.Post, request.Method);
            if (path.EndsWith("/single", StringComparison.Ordinal))
            {
                var item = await request.Content!.ReadFromJsonAsync<MeasurementSyncRequest>(cancellationToken);
                var result = Apply(item!);
                if (LoseResponse) { LoseResponse = false; throw new HttpRequestException("Втрачено відповідь після commit."); }
                if (WrongPayload && result.Measurement is { } measurement)
                    result = result with { Measurement = measurement with { Value = measurement.Value + 1 } };
                return new HttpResponseMessage((HttpStatusCode)result.Status) { Content = JsonContent.Create(result) };
            }
            Assert.Equal("/api/sync/measurements", path);
            var batch = await request.Content!.ReadFromJsonAsync<MeasurementSyncBatchRequest>(cancellationToken);
            BatchSizes.Add(batch!.Items!.Count);
            var results = batch.Items.Select(item => Apply(item!)).ToArray();
            if (LoseResponse) { LoseResponse = false; throw new HttpRequestException("Втрачено пакетну відповідь після commit."); }
            if (ReverseResults) Array.Reverse(results);
            if (OmitLastResult) results = results[..^1];
            return new HttpResponseMessage(HttpStatusCode.MultiStatus) { Content = JsonContent.Create(new MeasurementSyncBatchResult(results)) };
        }

        private MeasurementSyncResult Apply(MeasurementSyncRequest item)
        {
            var id = item.ExperimentId!.Value;
            var version = id == 7 ? Version : versions.GetValueOrDefault(id, 1);
            MeasurementSyncResult Failure(int status) => new(item.ClientId, status, false, null, version, new("Конфлікт або помилка виміру."));
            var duplicate = Measurements.SingleOrDefault(value => value.ClientId == item.ClientId);
            if (duplicate is not null)
                return duplicate.ExperimentId == id && duplicate.Value == item.Value && duplicate.MeasuredAt == item.MeasuredAt
                    ? new(item.ClientId, 200, true, duplicate, version, null) : Failure(409);
            if (item.ClientId == FailClientId) return Failure(500);
            if (version != item.ExpectedVersion) return Failure(409);
            Posts++;
            version++;
            if (id == 7) Version = version; else versions[id] = version;
            var measurement = new MeasurementDto(Posts, id, item.ClientId!.Value, item.Value!.Value, item.MeasuredAt!.Value, 1, "°C");
            Measurements.Add(measurement);
            return new(item.ClientId, 201, false, measurement, version, null);
        }
    }
}
