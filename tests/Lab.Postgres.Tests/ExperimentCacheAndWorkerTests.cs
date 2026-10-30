using System.Text.Json;
using Lab.Client.Maui.Data;
using Lab.Client.Maui.Services;
using Lab.Contracts.Experiments;
using Lab.Contracts.Instruments;
using Lab.Contracts.Sites;
using Lab.Infrastructure.Persistence;
using Lab.Postgres.Tests.Support;
using Lab.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Lab.Postgres.Tests;

[Collection(PostgresTestGroup.Name)]
public sealed class ExperimentCacheAndWorkerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Worker_StopsDuringDelay_WithoutErrorLog()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        await using var services = fixture.Account.OpenServices(fixture.Root);
        var logger = new WorkerLogger();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Worker:OutputDirectory"] = Path.Combine(fixture.Root, "hosted-exports"),
            ["Worker:IntervalSeconds"] = "300",
        }).Build();
        using var worker = new SyncWorker(logger, services.GetRequiredService<IServiceScopeFactory>(), configuration);
        await worker.StartAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!logger.Events.Any(item => item.Id == 3002)) await Task.Delay(10, timeout.Token);
        await worker.StopAsync(timeout.Token);
        Assert.DoesNotContain(logger.Events, item => item.Level >= LogLevel.Error);
    }
    [Fact]
    public async Task Cache_UsesSharedModel_AndPersistsRemoteVersion()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var series = Assert.Single(await new ExperimentCache(fixture.Account, fixture.Root).ListAsync());
        Assert.Equal(7, series.Id);
        Assert.Equal(4, series.Version);
        Assert.Equal("°C", series.Unit);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Cache.ValidateMeasurementAsync(7, 1000));
        Assert.Empty(await new ExperimentCache(fixture.Account with { UserId = 2 }, fixture.Root).ListAsync());
    }

    [Fact]
    public async Task IncompleteDownload_RollsBack_PreviousCacheRemainsReadable()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Cache.ReplaceAsync(Fixture.Sites, [], Fixture.Experiments));
        Assert.Equal(7, Assert.Single(await fixture.Cache.ListAsync()).Id);
    }

    [Fact]
    public async Task SignedSeries_CannotAcceptOfflineMeasurement()
    {
        using var fixture = new Fixture();
        var signed = Fixture.Experiments[0] with { Status = "SignedOff", SignedById = 1, SignedAt = DateTime.UtcNow };
        await fixture.Cache.ReplaceAsync(Fixture.Sites, [new InstrumentDto(3, 1, "Термометр", "SN-1", "°C", -50, 100, 0.1)], [signed]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Cache.ValidateMeasurementAsync(7, 12));
    }

    [Fact]
    public async Task Worker_RecalculationIsIdempotent_ExportsUtf8AndUpdatesChangedVersion()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        await using var services = fixture.Account.OpenServices(fixture.Root);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
        var series = await db.Experiments.Include(item => item.Instrument).SingleAsync();
        series.AddMeasurement(Guid.NewGuid(), 10, series.Unit, DateTime.UtcNow);
        series.AddMeasurement(Guid.NewGuid(), 12, series.Unit, DateTime.UtcNow);
        await db.SaveChangesAsync();
        var output = Path.Combine(fixture.Root, "exports");
        var processor = new SeriesProcessor(db, output);
        Assert.True(await processor.ProcessAsync(7));
        var manifest = Assert.Single(Directory.GetFiles(output, "*.manifest.json"));
        var first = await File.ReadAllTextAsync(manifest);
        Assert.False(await processor.ProcessAsync(7));
        Assert.Equal(first, await File.ReadAllTextAsync(manifest));
        var cache = JsonSerializer.Deserialize<SeriesCache>(first, JsonOptions);
        Assert.Equal(11, cache!.Mean);
        Assert.Equal(2, cache.Count);
        Assert.Contains("°C", await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(output, "*.csv"))));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(output, "series-*.json"), path => !path.EndsWith(".manifest.json", StringComparison.Ordinal))));
        Assert.Equal(2, json.RootElement.GetArrayLength());
        series.AddMeasurement(Guid.NewGuid(), 14, series.Unit, DateTime.UtcNow);
        await db.SaveChangesAsync();
        Assert.True(await processor.ProcessAsync(7));
        Assert.Equal(2, Directory.GetFiles(output, "*.manifest.json").Length);
        Assert.Empty(Directory.GetFiles(output, "*.tmp"));
    }

    [Fact]
    public async Task Worker_CancellationPublishesNoManifest_AndCleanupKeepsCompletedFiles()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        await using var services = fixture.Account.OpenServices(fixture.Root);
        await using var scope = services.CreateAsyncScope();
        var processor = new SeriesProcessor(scope.ServiceProvider.GetRequiredService<LabDbContext>(), fixture.Root);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ProcessAsync(7, cancellation.Token));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.manifest.json"));
        var stale = Path.Combine(fixture.Root, "series-old.tmp");
        var complete = Path.Combine(fixture.Root, "series-good.csv");
        await File.WriteAllTextAsync(stale, "partial");
        await File.WriteAllTextAsync(complete, "complete");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-2));
        processor.CleanTemporaryFiles(DateTime.UtcNow.AddDays(-1));
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(complete));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string? previousProvider = Environment.GetEnvironmentVariable("LAB_DB_PROVIDER");
        public Fixture() { Directory.CreateDirectory(Root); Environment.SetEnvironmentVariable("LAB_DB_PROVIDER", "Sqlite"); }
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "sigma-cache-" + Guid.NewGuid().ToString("N"));
        public ClientAccount Account { get; } = new("https://lab.example/", 1, "Юра", "yura@example.org", "token", DateTime.UtcNow.AddHours(1));
        public ExperimentCache Cache => new(Account, Root);
        public static SiteDto[] Sites => [new(2, "Майданчик", 50, 30, DateTime.UtcNow)];
        public static ExperimentDto[] Experiments => [new(7, "Серія", 2, 3, null, "Draft", DateTime.UtcNow, null, null, 4, "°C")];
        public Task SeedAsync() => Cache.ReplaceAsync(Sites, [new InstrumentDto(3, 1, "Термометр", "SN-1", "°C", -50, 100, 0.1)], Experiments);
        public void Dispose() { Environment.SetEnvironmentVariable("LAB_DB_PROVIDER", previousProvider); Directory.Delete(Root, true); }
    }

    private sealed class WorkerLogger : ILogger<SyncWorker>
    {
        public ConcurrentQueue<(LogLevel Level, int Id)> Events { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Events.Enqueue((logLevel, eventId.Id));
    }
}
