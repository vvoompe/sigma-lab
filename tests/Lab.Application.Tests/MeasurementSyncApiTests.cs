using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lab.Contracts.Sync;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lab.Application.Tests;

public sealed class MeasurementSyncApiTests
{
    [Fact]
    public async Task Lost_response_retry_is_duplicate_even_after_version_changed_and_signing()
    {
        await using var fixture = await FixtureAsync();
        var series = await fixture.CreateExperimentAsync();
        var request = Item(series.Id, 1);
        var created = await SingleAsync(fixture, request, HttpStatusCode.Created);
        Assert.False(created.Duplicate);
        Assert.Equal(2, created.ExperimentVersion);
        await fixture.AddAsync(series.Id, 2, 3);
        await using (var db = fixture.CreateContext())
        {
            var experiment = await db.Experiments.SingleAsync();
            experiment.Sign(fixture.OtherId, DateTime.UtcNow);
            await db.SaveChangesAsync();
        }
        var repeated = await SingleAsync(fixture, request, HttpStatusCode.OK);
        Assert.True(repeated.Duplicate);
        Assert.Equal(created.Measurement!.Id, repeated.Measurement!.Id);
        Assert.Equal(4, repeated.ExperimentVersion);
        await using var stored = fixture.CreateContext();
        Assert.Equal(2, await stored.Measurements.CountAsync());
    }

    [Fact]
    public async Task Mixed_batch_keeps_successful_items_and_returns_current_version_in_order()
    {
        await using var fixture = await FixtureAsync();
        var series = await fixture.CreateExperimentAsync();
        var first = Item(series.Id, 1);
        var invalid = Item(series.Id, 2) with { Value = 101 };
        var stale = Item(series.Id, 1);
        var last = Item(series.Id, 2);
        using var response = await fixture.Client.PostAsJsonAsync("/api/sync/measurements",
            new MeasurementSyncBatchRequest { Items = [first, invalid, stale, last] });
        Assert.Equal(HttpStatusCode.MultiStatus, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<MeasurementSyncBatchResult>())!;
        Assert.Equal([201, 400, 409, 201], result.Items.Select(item => item.Status));
        Assert.Equal([first.ClientId, invalid.ClientId, stale.ClientId, last.ClientId], result.Items.Select(item => item.ClientId));
        Assert.Equal(2, result.Items[2].ExperimentVersion);
        Assert.Null(result.Items[1].Measurement);
        await using var stored = fixture.CreateContext();
        Assert.Equal(2, await stored.Measurements.CountAsync());
        Assert.Equal(3, (await stored.Experiments.SingleAsync()).Version);
    }

    [Fact]
    public async Task Fifty_measurements_and_repeated_batch_have_no_duplicates()
    {
        await using var fixture = await FixtureAsync();
        var series = await fixture.CreateExperimentAsync();
        var items = Enumerable.Range(1, 50).Select(version => Item(series.Id, version)).ToArray();
        foreach (var expected in new[] { 201, 200 })
        {
            using var response = await fixture.Client.PostAsJsonAsync("/api/sync/measurements", new MeasurementSyncBatchRequest { Items = items });
            Assert.Equal(HttpStatusCode.MultiStatus, response.StatusCode);
            var result = (await response.Content.ReadFromJsonAsync<MeasurementSyncBatchResult>())!;
            Assert.Equal(50, result.Items.Count);
            Assert.All(result.Items, item => { Assert.Equal(expected, item.Status); Assert.Equal(expected == 200, item.Duplicate); });
        }
        await using var db = fixture.CreateContext();
        Assert.Equal(50, await db.Measurements.CountAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task Envelope_size_is_limited(int size)
    {
        await using var fixture = await FixtureAsync();
        using var response = await fixture.Client.PostAsJsonAsync("/api/sync/measurements",
            new MeasurementSyncBatchRequest { Items = Enumerable.Range(1, size).Select(x => Item(1, x)).ToArray() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Null_and_missing_fields_are_item_errors_not_default_zero_writes()
    {
        await using var fixture = await FixtureAsync();
        using var response = await fixture.Client.PostAsJsonAsync("/api/sync/measurements",
            new MeasurementSyncBatchRequest { Items = [null, new MeasurementSyncRequest()] });
        Assert.Equal(HttpStatusCode.MultiStatus, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<MeasurementSyncBatchResult>())!;
        Assert.All(result.Items, item => Assert.Equal(400, item.Status));
        await using var db = fixture.CreateContext();
        Assert.Empty(await db.Measurements.ToListAsync());
    }

    [Fact]
    public async Task Foreign_access_payload_mismatch_and_same_uuid_other_series_do_not_disclose_measurement()
    {
        await using var fixture = await FixtureAsync();
        var series = await fixture.CreateExperimentAsync();
        var otherSeries = await fixture.CreateExperimentAsync();
        var original = Item(series.Id, 1);
        await SingleAsync(fixture, original, HttpStatusCode.Created);
        foreach (var request in new[] { original with { Value = 98 }, original with { MeasuredAt = original.MeasuredAt!.Value.AddSeconds(1) }, original with { ExperimentId = otherSeries.Id } })
        {
            using var conflict = await fixture.Client.PostAsJsonAsync("/api/sync/measurements/single", request);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.DoesNotContain("measurement", await conflict.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        fixture.Authenticate(userId: fixture.OtherId);
        using var forbidden = await fixture.Client.PostAsJsonAsync("/api/sync/measurements/single", original);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.DoesNotContain("experimentVersion", await forbidden.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        fixture.Authenticate("LabManager");
        using var manager = await fixture.Client.PostAsJsonAsync("/api/sync/measurements/single", original);
        Assert.Equal(HttpStatusCode.Forbidden, manager.StatusCode);
        fixture.Client.DefaultRequestHeaders.Authorization = null;
        using var anonymous = await fixture.Client.PostAsJsonAsync("/api/sync/measurements/single", original);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Concurrent_committed_uuid_is_rechecked_in_new_context()
    {
        await using var fixture = await FixtureAsync();
        await CheckConcurrentAsync(fixture, foreignPayload: false);
    }

    [Fact]
    public async Task Concurrency_with_different_uuid_returns_version_without_partial_insert()
    {
        await using var fixture = await FixtureAsync();
        var series = await fixture.CreateExperimentAsync();
        fixture.BeforeSave.Callback = async () =>
        {
            await using var db = fixture.CreateContext();
            var item = await db.Experiments.Include(x => x.Instrument).Include(x => x.Measurements).SingleAsync();
            item.AddMeasurement(Guid.NewGuid(), 7, item.Unit, DateTime.UtcNow);
            await db.SaveChangesAsync();
        };
        using var response = await fixture.Client.PostAsJsonAsync("/api/sync/measurements", new MeasurementSyncBatchRequest { Items = [Item(series.Id, 1)] });
        var result = (await response.Content.ReadFromJsonAsync<MeasurementSyncBatchResult>())!;
        Assert.Equal(409, result.Items[0].Status);
        Assert.Equal(2, result.Items[0].ExperimentVersion);
        await using var stored = fixture.CreateContext();
        Assert.Single(await stored.Measurements.ToListAsync());
    }

    [Fact]
    public async Task Unknown_database_failure_is_500_and_next_item_still_commits()
    {
        await using var fixture = await FixtureAsync();
        var series = await fixture.CreateExperimentAsync();
        fixture.BeforeSave.Callback = () => throw new DbUpdateException("internal-database-details");
        using var response = await fixture.Client.PostAsJsonAsync("/api/sync/measurements",
            new MeasurementSyncBatchRequest { Items = [Item(series.Id, 1), Item(series.Id, 1)] });
        Assert.Equal(HttpStatusCode.MultiStatus, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<MeasurementSyncBatchResult>())!;
        Assert.Equal([500, 201], result.Items.Select(item => item.Status));
        Assert.DoesNotContain("internal-database-details", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var stored = fixture.CreateContext();
        Assert.Single(await stored.Measurements.ToListAsync());
    }

    [Fact]
    public async Task Missing_series_is_item_404_without_version()
    {
        await using var fixture = await FixtureAsync();
        using var response = await fixture.Client.PostAsJsonAsync("/api/sync/measurements",
            new MeasurementSyncBatchRequest { Items = [Item(int.MaxValue, 1)] });
        Assert.Equal(HttpStatusCode.MultiStatus, response.StatusCode);
        var item = Assert.Single((await response.Content.ReadFromJsonAsync<MeasurementSyncBatchResult>())!.Items);
        Assert.Equal(404, item.Status);
        Assert.Null(item.ExperimentVersion);
        Assert.Null(item.Measurement);
    }

    [Fact]
    public async Task Concurrent_deletion_is_conflict_without_insert_or_foreign_data()
    {
        await using var fixture = await FixtureAsync();
        var series = await fixture.CreateExperimentAsync();
        fixture.BeforeSave.Callback = async () =>
        {
            await using var db = fixture.CreateContext();
            db.Experiments.Remove(await db.Experiments.SingleAsync());
            await db.SaveChangesAsync();
        };
        using var response = await fixture.Client.PostAsJsonAsync("/api/sync/measurements/single", Item(series.Id, 1));
        Assert.True(response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound);
        await using var stored = fixture.CreateContext();
        Assert.Empty(await stored.Measurements.ToListAsync());
    }

    [Fact]
    public async Task New_measurement_after_signing_and_invalid_utc_are_rejected()
    {
        await using var fixture = await FixtureAsync();
        var series = await fixture.CreateExperimentAsync();
        using var invalid = await fixture.Client.PostAsJsonAsync("/api/sync/measurements/single",
            Item(series.Id, 1) with { MeasuredAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified) });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        await using (var db = fixture.CreateContext())
        {
            (await db.Experiments.SingleAsync()).Sign(fixture.OtherId, DateTime.UtcNow);
            await db.SaveChangesAsync();
        }
        using var signed = await fixture.Client.PostAsJsonAsync("/api/sync/measurements/single", Item(series.Id, 2));
        Assert.Equal(HttpStatusCode.Conflict, signed.StatusCode);
        await using var stored = fixture.CreateContext();
        Assert.Empty(await stored.Measurements.ToListAsync());
    }

    [ResourcePostgresFact]
    public async Task PostgreSQL_concurrent_uuid_retry_and_microseconds_are_idempotent()
    {
        await using var fixture = await FixtureAsync(PostgresConnection());
        await CheckConcurrentAsync(fixture, foreignPayload: false);
    }

    [ResourcePostgresFact]
    public async Task PostgreSQL_concurrent_other_payload_is_conflict()
    {
        await using var fixture = await FixtureAsync(PostgresConnection());
        await CheckConcurrentAsync(fixture, foreignPayload: true);
    }

    [Fact]
    public async Task OpenApi_describes_batch_207_single_200_201_and_envelope_limit()
    {
        await using var fixture = await FixtureAsync();
        using var schema = JsonDocument.Parse(await fixture.Client.GetStringAsync("/openapi/v1.json"));
        var paths = schema.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/sync/measurements").GetProperty("post").GetProperty("responses").TryGetProperty("207", out _));
        var responses = paths.GetProperty("/api/sync/measurements/single").GetProperty("post").GetProperty("responses");
        Assert.True(responses.TryGetProperty("200", out _));
        Assert.True(responses.TryGetProperty("201", out _));
        var items = schema.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("MeasurementSyncBatchRequest").GetProperty("properties").GetProperty("items");
        Assert.Equal(1, items.GetProperty("minItems").GetInt32());
        Assert.Equal(50, items.GetProperty("maxItems").GetInt32());
    }

    private static async Task CheckConcurrentAsync(ResourceApiFixture fixture, bool foreignPayload)
    {
        var series = await fixture.CreateExperimentAsync();
        var request = Item(series.Id, 1);
        fixture.BeforeSave.Callback = async () =>
        {
            await using var competing = fixture.CreateContext();
            var item = await competing.Experiments.Include(x => x.Instrument).Include(x => x.Measurements).SingleAsync();
            item.AddMeasurement(request.ClientId!.Value, foreignPayload ? 99 : request.Value!.Value,
                item.Unit, request.MeasuredAt!.Value);
            await competing.SaveChangesAsync();
        };
        using var response = await fixture.Client.PostAsJsonAsync("/api/sync/measurements/single", request);
        Assert.Equal(foreignPayload ? HttpStatusCode.Conflict : HttpStatusCode.OK, response.StatusCode);
        if (!foreignPayload)
        {
            var result = (await response.Content.ReadFromJsonAsync<MeasurementSyncResult>())!;
            Assert.True(result.Duplicate);
        }
        using var retry = await fixture.Client.PostAsJsonAsync("/api/sync/measurements/single", request);
        Assert.Equal(response.StatusCode, retry.StatusCode);
        await using var stored = fixture.CreateContext();
        Assert.Single(await stored.Measurements.ToListAsync());
        Assert.Equal(2, (await stored.Experiments.SingleAsync()).Version);
    }

    private static MeasurementSyncRequest Item(int id, long version) => new()
    {
        ExperimentId = id, ClientId = Guid.NewGuid(), Value = 2.5,
        MeasuredAt = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc).AddTicks(123), ExpectedVersion = version,
    };

    private static async Task<ResourceApiFixture> FixtureAsync(string? connection = null)
    {
        var fixture = new ResourceApiFixture(connection);
        await fixture.InitializeAsync();
        return fixture;
    }

    private static async Task<MeasurementSyncResult> SingleAsync(ResourceApiFixture fixture, MeasurementSyncRequest request, HttpStatusCode expected)
    {
        using var response = await fixture.Client.PostAsJsonAsync("/api/sync/measurements/single", request);
        Assert.Equal(expected, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MeasurementSyncResult>())!;
    }

    private static string PostgresConnection() => new NpgsqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("LAB_TEST_POSTGRES_CONNECTION")
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
        ?? throw new InvalidOperationException("Задайте окреме тестове підключення PostgreSQL."))
        { Database = "resource_api_" + Guid.NewGuid().ToString("N") }.ConnectionString;
}
