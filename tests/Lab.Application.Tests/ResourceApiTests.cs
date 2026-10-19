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

public sealed class ResourceApiTests : IAsyncLifetime, IAsyncDisposable
{
    private readonly ResourceApiFixture fixture = new();
    private HttpClient Client => fixture.Client;
    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();
    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsync());

    [Fact]
    public async Task Instrument_CRUD_preserves_metrology_and_location()
    {
        fixture.Authenticate("Administrator");
        using var created = await Client.PostAsJsonAsync("/api/instruments", InstrumentRequest("Serial-A"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var item = await created.Content.ReadFromJsonAsync<InstrumentDto>();
        Assert.NotNull(item);
        Assert.Equal($"/api/instruments/{item.Id}", created.Headers.Location?.AbsolutePath);
        using var update = await Client.PutAsJsonAsync(created.Headers.Location, new { name = " Нова назва ", minValue = -999, ownerId = fixture.OtherId });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        fixture.Authenticate();
        var stored = await Client.GetFromJsonAsync<InstrumentDto>(created.Headers.Location);
        Assert.NotNull(stored);
        Assert.Equal("Нова назва", stored.Name);
        Assert.Equal(0, stored.MinValue);
        Assert.Equal(fixture.OwnerId, stored.OwnerId);
        Assert.Equal("Serial-A", stored.SerialNumber);
        var list = await Client.GetFromJsonAsync<PagedResult<InstrumentDto>>("/api/instruments?name=Нова&pageSize=999");
        Assert.NotNull(list);
        Assert.Equal(100, list.PageSize);
        Assert.Single(list.Items);
        fixture.Authenticate("Administrator");
        using var deleted = await Client.DeleteAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await AssertProblemAsync(await Client.GetAsync(created.Headers.Location), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Instrument_serial_number_is_global_case_sensitive_and_trimmed()
    {
        fixture.Authenticate("Administrator");
        using var first = await Client.PostAsJsonAsync("/api/instruments", InstrumentRequest(" Serial-X "));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        await AssertProblemAsync(await Client.PostAsJsonAsync("/api/instruments", InstrumentRequest("Serial-X")), HttpStatusCode.Conflict);
        using var lower = await Client.PostAsJsonAsync("/api/instruments", InstrumentRequest("serial-x"));
        Assert.Equal(HttpStatusCode.Created, lower.StatusCode);
    }

    [Fact]
    public async Task Instrument_write_requires_administrator_and_read_hides_foreign()
    {
        await AssertProblemAsync(await Client.PostAsJsonAsync("/api/instruments", InstrumentRequest("new")), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await Client.PutAsJsonAsync($"/api/instruments/{fixture.InstrumentId}", new { name = "X" }), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await Client.DeleteAsync($"/api/instruments/{fixture.InstrumentId}"), HttpStatusCode.Forbidden);
        fixture.Authenticate(userId: fixture.OtherId);
        await AssertProblemAsync(await Client.GetAsync($"/api/instruments/{fixture.InstrumentId}"), HttpStatusCode.NotFound);
        var list = await Client.GetFromJsonAsync<PagedResult<InstrumentDto>>("/api/instruments");
        Assert.NotNull(list);
        Assert.Equal(0, list.TotalCount);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"ownerId\":1,\"name\":\"x\",\"serialNumber\":\"s\",\"unit\":\"m\",\"maxValue\":1,\"absoluteError\":0}")]
    [InlineData("{\"ownerId\":1,\"name\":\"x\",\"serialNumber\":\"s\",\"unit\":\"m\",\"minValue\":2,\"maxValue\":1,\"absoluteError\":0}")]
    [InlineData("{\"ownerId\":1,\"name\":\"x\",\"serialNumber\":\"s\",\"unit\":\"m\",\"minValue\":0,\"maxValue\":1,\"absoluteError\":-1}")]
    public async Task Invalid_instrument_returns_400(string body)
    {
        fixture.Authenticate("Administrator");
        await AssertProblemAsync(await PostJsonAsync("/api/instruments", body), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Experiment_CRUD_restricts_update_and_deletion_is_audited()
    {
        var item = await fixture.CreateExperimentAsync();
        using var renamed = await Client.PutAsJsonAsync($"/api/experiments/{item.Id}", new
        { name = " Нова серія ", expectedVersion = item.Version, instrumentId = int.MaxValue, ownerId = fixture.OtherId, status = "SignedOff" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var changed = await renamed.Content.ReadFromJsonAsync<ExperimentDto>();
        Assert.NotNull(changed);
        Assert.Equal(item.Version + 1, changed.Version);
        Assert.Equal(fixture.InstrumentId, changed.InstrumentId);
        Assert.Equal("Draft", changed.Status);
        Assert.Equal("Нова серія", changed.Name);
        var read = await Client.GetFromJsonAsync<ExperimentDto>($"/api/experiments/{item.Id}");
        Assert.Equal(changed, read);
        var page = await Client.GetFromJsonAsync<PagedResult<ExperimentDto>>($"/api/experiments?siteId={fixture.SiteId}&status=Draft&name=Нова&pageSize=999");
        Assert.NotNull(page);
        Assert.Single(page.Items);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(100, page.PageSize);
        await fixture.AddAsync(item.Id, changed.Version, 1);
        using var deleted = await Client.DeleteAsync($"/api/experiments/{item.Id}?expectedVersion={changed.Version + 1}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await AssertProblemAsync(await Client.GetAsync($"/api/experiments/{item.Id}"), HttpStatusCode.NotFound);
        await using var db = fixture.CreateContext();
        Assert.Empty(await db.Measurements.ToListAsync());
        var audit = await db.AuditEntries.SingleAsync();
        Assert.Equal("experiment.deleted", audit.Action);
        Assert.Equal(fixture.OwnerId, audit.ActorId);
        Assert.Equal(item.Id, audit.TargetId);
    }

    [Fact]
    public async Task Foreign_experiment_read_is_404_mutations_403_and_count_is_private()
    {
        var item = await fixture.CreateExperimentAsync();
        fixture.Authenticate(userId: fixture.OtherId);
        foreach (var path in new[] { $"/api/experiments/{item.Id}", $"/api/experiments/{item.Id}/statistics", $"/api/experiments/{item.Id}/measurements", $"/api/experiments/{item.Id}/measurements/1" })
            await AssertProblemAsync(await Client.GetAsync(path), HttpStatusCode.NotFound);
        await AssertProblemAsync(await Client.PutAsJsonAsync($"/api/experiments/{item.Id}", new { name = "x", expectedVersion = 999 }), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await Client.DeleteAsync($"/api/experiments/{item.Id}?expectedVersion=999"), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await Client.PostAsJsonAsync($"/api/experiments/{item.Id}/measurements", ResourceApiFixture.Measurement(999, 1)), HttpStatusCode.Forbidden);
        var page = await Client.GetFromJsonAsync<PagedResult<ExperimentDto>>("/api/experiments");
        Assert.NotNull(page);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Creating_with_foreign_instrument_or_site_is_404()
    {
        await using (var db = fixture.CreateContext())
        {
            var foreign = new Instrument(fixture.OtherId, "Чужий", "Other", "m", 0, 100, 1);
            db.Instruments.Add(foreign);
            await db.SaveChangesAsync();
            await AssertProblemAsync(await Client.PostAsJsonAsync("/api/experiments", new { name = "x", siteId = fixture.SiteId, instrumentId = foreign.Id }), HttpStatusCode.NotFound);
        }
        fixture.Authenticate(userId: fixture.OtherId);
        await AssertProblemAsync(await Client.PostAsJsonAsync("/api/experiments", new { name = "x", siteId = fixture.SiteId, instrumentId = fixture.InstrumentId }), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Measurement_location_range_unit_uuid_and_expected_version()
    {
        var item = await fixture.CreateExperimentAsync();
        using var response = await Client.PostAsJsonAsync($"/api/experiments/{item.Id}/measurements", ResourceApiFixture.Measurement(1, 0));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<MeasurementMutationDto>();
        Assert.NotNull(result);
        Assert.Equal(2, result.ExperimentVersion);
        Assert.Equal("m", result.Measurement.Unit);
        Assert.Equal(0, result.Measurement.Value);
        var read = await Client.GetFromJsonAsync<MeasurementDto>(response.Headers.Location);
        Assert.Equal(result.Measurement, read);
        await AssertProblemAsync(await Client.PostAsJsonAsync($"/api/experiments/{item.Id}/measurements", ResourceApiFixture.Measurement(1, 2)), HttpStatusCode.Conflict);
        await AssertProblemAsync(await Client.PostAsJsonAsync($"/api/experiments/{item.Id}/measurements", ResourceApiFixture.Measurement(2, 101)), HttpStatusCode.BadRequest);
        await AssertProblemAsync(await Client.PostAsJsonAsync($"/api/experiments/{item.Id}/measurements", new { clientId = result.Measurement.ClientId, value = 99, measuredAt = DateTime.UtcNow, expectedVersion = 2 }), HttpStatusCode.Conflict);
        var page = await Client.GetFromJsonAsync<PagedResult<MeasurementDto>>($"/api/experiments/{item.Id}/measurements?pageSize=999");
        Assert.NotNull(page);
        Assert.Single(page.Items);
        Assert.Equal(100, page.PageSize);
        await AssertProblemAsync(await Client.GetAsync($"/api/experiments/{item.Id}/measurements/99999"), HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"clientId\":\"00000000-0000-0000-0000-000000000000\",\"value\":1,\"measuredAt\":\"2026-10-19T12:00:00Z\",\"expectedVersion\":1}")]
    [InlineData("{\"clientId\":\"11111111-1111-1111-1111-111111111111\",\"value\":null,\"measuredAt\":\"2026-10-19T12:00:00Z\",\"expectedVersion\":1}")]
    [InlineData("{\"clientId\":\"11111111-1111-1111-1111-111111111111\",\"value\":1,\"measuredAt\":\"2026-10-19T12:00:00\",\"expectedVersion\":1}")]
    [InlineData("{\"clientId\":\"11111111-1111-1111-1111-111111111111\",\"value\":1e400,\"measuredAt\":\"2026-10-19T12:00:00Z\",\"expectedVersion\":1}")]
    public async Task Invalid_measurement_does_not_change_series(string body)
    {
        var item = await fixture.CreateExperimentAsync();
        await AssertProblemAsync(await PostJsonAsync($"/api/experiments/{item.Id}/measurements", body), HttpStatusCode.BadRequest);
        await using var db = fixture.CreateContext();
        Assert.Equal(1, (await db.Experiments.SingleAsync()).Version);
        Assert.Empty(await db.Measurements.ToListAsync());
    }

    [Fact]
    public async Task Signed_series_rejects_all_supported_mutations()
    {
        var item = await fixture.CreateExperimentAsync();
        await using (var db = fixture.CreateContext())
        {
            var experiment = await db.Experiments.SingleAsync();
            experiment.Sign(fixture.OtherId, DateTime.UtcNow);
            await db.SaveChangesAsync();
        }
        await AssertProblemAsync(await Client.PutAsJsonAsync($"/api/experiments/{item.Id}", new { name = "x", expectedVersion = 2 }), HttpStatusCode.Conflict);
        await AssertProblemAsync(await Client.DeleteAsync($"/api/experiments/{item.Id}?expectedVersion=2"), HttpStatusCode.Conflict);
        await AssertProblemAsync(await Client.PostAsJsonAsync($"/api/experiments/{item.Id}/measurements", ResourceApiFixture.Measurement(2, 1)), HttpStatusCode.Conflict);
        await using var stored = fixture.CreateContext();
        Assert.Empty(await stored.AuditEntries.ToListAsync());
    }

    [Fact]
    public async Task Statistics_uses_stored_unit_sample_deviation_Student_interval_and_reference()
    {
        var item = await fixture.CreateExperimentAsync(reference: true);
        await AssertProblemAsync(await Client.GetAsync($"/api/experiments/{item.Id}/statistics"), HttpStatusCode.BadRequest);
        await fixture.AddAsync(item.Id, 1, 1);
        await fixture.AddAsync(item.Id, 2, 2);
        await fixture.AddAsync(item.Id, 3, 3);
        var stats = await Client.GetFromJsonAsync<ExperimentStatisticsDto>($"/api/experiments/{item.Id}/statistics?confidenceLevel=0.95&unit=forged");
        Assert.NotNull(stats);
        Assert.Equal(3, stats.Count);
        Assert.Equal(4, stats.ExperimentVersion);
        Assert.Equal("m", stats.Unit);
        Assert.Equal(2, stats.Mean, 10);
        Assert.Equal(1, stats.SampleStandardDeviation, 10);
        Assert.Equal(1 / Math.Sqrt(3), stats.StandardError, 10);
        Assert.Equal(-0.4841377117, stats.ConfidenceInterval.Lower, 8);
        Assert.Equal(4.4841377117, stats.ConfidenceInterval.Upper, 8);
        Assert.Equal(0.1, stats.InstrumentAbsoluteError);
        Assert.NotNull(stats.ReferenceComparison);
        Assert.Equal(1, stats.ReferenceComparison.AbsoluteDeviation);
        Assert.True(stats.ReferenceComparison.WithinTolerance);
        Assert.Empty(stats.OutlierMeasurementIds);
    }

    [Fact]
    public async Task Outlier_is_marked_without_deleting_and_numeric_overflow_is_400()
    {
        var item = await fixture.CreateExperimentAsync();
        for (var i = 0; i < 11; i++) await fixture.AddAsync(item.Id, i + 1, 0);
        var outlier = await fixture.AddAsync(item.Id, 12, 100);
        var stats = await Client.GetFromJsonAsync<ExperimentStatisticsDto>($"/api/experiments/{item.Id}/statistics");
        Assert.NotNull(stats);
        Assert.Equal(outlier.Measurement.Id, Assert.Single(stats.OutlierMeasurementIds));
        Assert.Null(stats.ReferenceComparison);
        await using var db = fixture.CreateContext();
        Assert.Equal(12, await db.Measurements.CountAsync());
        var extreme = new Instrument(fixture.OwnerId, "Extreme", "Extreme", "large", -1e308, 1e308, 0);
        var series = new Experiment(fixture.SiteId, fixture.OwnerId, "Extreme", extreme);
        series.AddMeasurement(Guid.NewGuid(), -1e308, "large", DateTime.UtcNow);
        series.AddMeasurement(Guid.NewGuid(), 1e308, "large", DateTime.UtcNow);
        db.Experiments.Add(series);
        await db.SaveChangesAsync();
        await AssertProblemAsync(await Client.GetAsync($"/api/experiments/{series.Id}/statistics"), HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/api/instruments?page=0")]
    [InlineData("/api/experiments?pageSize=0")]
    [InlineData("/api/experiments?status=Unknown")]
    [InlineData("/api/experiments?siteId=-1")]
    [InlineData("/api/instruments/-1")]
    public async Task Invalid_queries_are_400(string path) => await AssertProblemAsync(await Client.GetAsync(path), HttpStatusCode.BadRequest);

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public async Task Invalid_confidence_is_400(string confidence)
    {
        var item = await fixture.CreateExperimentAsync();
        await AssertProblemAsync(await Client.GetAsync($"/api/experiments/{item.Id}/statistics?confidenceLevel={confidence}"), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Paging_beyond_Int32_returns_empty_without_overflow()
    {
        var item = await fixture.CreateExperimentAsync();
        foreach (var path in new[] { "/api/instruments", "/api/experiments" })
        {
            using var response = await Client.GetAsync(path + "?page=2147483647&pageSize=2147483647");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(0, json.RootElement.GetProperty("items").GetArrayLength());
            Assert.Equal(1, json.RootElement.GetProperty("totalCount").GetInt32());
        }
        var measurements = await Client.GetFromJsonAsync<PagedResult<MeasurementDto>>($"/api/experiments/{item.Id}/measurements?page=2147483647&pageSize=2147483647");
        Assert.NotNull(measurements);
        Assert.Empty(measurements.Items);
    }

    [Fact]
    public async Task Missing_and_stale_versions_are_rejected_and_parent_instrument_is_restricted()
    {
        var item = await fixture.CreateExperimentAsync();
        await AssertProblemAsync(await Client.PutAsJsonAsync($"/api/experiments/{item.Id}", new { name = "x" }), HttpStatusCode.BadRequest);
        await AssertProblemAsync(await Client.DeleteAsync($"/api/experiments/{item.Id}"), HttpStatusCode.BadRequest);
        await AssertProblemAsync(await Client.PutAsJsonAsync($"/api/experiments/{item.Id}", new { name = "x", expectedVersion = 999 }), HttpStatusCode.Conflict);
        await AssertProblemAsync(await Client.DeleteAsync($"/api/experiments/{item.Id}?expectedVersion=999"), HttpStatusCode.Conflict);
        fixture.Authenticate("Administrator");
        await AssertProblemAsync(await Client.DeleteAsync($"/api/instruments/{fixture.InstrumentId}"), HttpStatusCode.Conflict);
        await AssertProblemAsync(await Client.PostAsJsonAsync("/api/instruments", InstrumentRequest("Missing") with { OwnerId = int.MaxValue }), HttpStatusCode.NotFound);
        await AssertProblemAsync(await Client.PutAsJsonAsync("/api/instruments/99999", new { name = "X" }), HttpStatusCode.NotFound);
        await AssertProblemAsync(await Client.DeleteAsync("/api/instruments/99999"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Concurrent_series_change_returns_409_and_rolls_back_delete_audit()
    {
        var item = await fixture.CreateExperimentAsync();
        fixture.BeforeSave.Callback = async () =>
        {
            await using var competing = fixture.CreateContext();
            var experiment = await competing.Experiments.SingleAsync();
            experiment.Rename("Переможець");
            await competing.SaveChangesAsync();
        };
        await AssertProblemAsync(await Client.DeleteAsync($"/api/experiments/{item.Id}?expectedVersion=1"), HttpStatusCode.Conflict);
        await using var stored = fixture.CreateContext();
        Assert.Equal("Переможець", (await stored.Experiments.SingleAsync()).Name);
        Assert.Empty(await stored.AuditEntries.ToListAsync());
    }

    [Fact]
    public async Task Concurrent_measurements_conflict_on_series_version_without_silent_overwrite()
    {
        var item = await fixture.CreateExperimentAsync();
        fixture.BeforeSave.Callback = async () =>
        {
            await using var competing = fixture.CreateContext();
            var experiment = await competing.Experiments.Include(x => x.Instrument).Include(x => x.Measurements).SingleAsync();
            experiment.AddMeasurement(Guid.NewGuid(), 7, experiment.Unit, DateTime.UtcNow);
            await competing.SaveChangesAsync();
        };
        await AssertProblemAsync(await Client.PostAsJsonAsync($"/api/experiments/{item.Id}/measurements", ResourceApiFixture.Measurement(1, 9)), HttpStatusCode.Conflict);
        await using var stored = fixture.CreateContext();
        Assert.Equal(7, (await stored.Measurements.SingleAsync()).Value);
        Assert.Equal(2, (await stored.Experiments.SingleAsync()).Version);
    }

    [Fact]
    public async Task OpenApi_contains_every_route_responses_and_required_payload_fields()
    {
        using var response = await Client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = json.RootElement.GetProperty("paths");
        foreach (var (path, methods) in new (string, string[])[] {
            ("/api/instruments", ["get", "post"]), ("/api/instruments/{id}", ["get", "put", "delete"]),
            ("/api/experiments", ["get", "post"]), ("/api/experiments/{id}", ["get", "put", "delete"]),
            ("/api/experiments/{id}/measurements", ["get", "post"]),
            ("/api/experiments/{id}/measurements/{measurementId}", ["get"]),
            ("/api/experiments/{id}/statistics", ["get"]) })
        {
            foreach (var method in methods)
            {
                var operation = paths.GetProperty(path).GetProperty(method);
                foreach (var code in new[] { "400", "401", "403", "404", "409" })
                    Assert.True(operation.GetProperty("responses").TryGetProperty(code, out _), $"{method} {path}: {code}");
            }
        }
        var schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (var (schema, fields) in new (string, string[])[] {
            ("CreateInstrumentRequest", ["ownerId", "minValue", "maxValue", "absoluteError", "name", "serialNumber", "unit"]),
            ("CreateExperimentRequest", ["name", "siteId", "instrumentId"]),
            ("AddMeasurementRequest", ["clientId", "value", "measuredAt", "expectedVersion"]),
            ("RenameExperimentRequest", ["name", "expectedVersion"]) })
        {
            var required = schemas.GetProperty(schema).GetProperty("required").EnumerateArray().Select(x => x.GetString()).ToArray();
            foreach (var field in fields) Assert.Contains(field, required);
        }
    }

    [Fact]
    public async Task Anonymous_requests_are_401_and_unassigned_manager_cannot_mutate()
    {
        var item = await fixture.CreateExperimentAsync();
        Client.DefaultRequestHeaders.Authorization = null;
        foreach (var path in new[] { "/api/instruments", "/api/experiments", $"/api/experiments/{item.Id}/statistics" })
        {
            using var response = await Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        fixture.Authenticate("LabManager", fixture.OtherId);
        await AssertProblemAsync(await Client.GetAsync($"/api/experiments/{item.Id}"), HttpStatusCode.NotFound);
        await AssertProblemAsync(await Client.PutAsJsonAsync($"/api/experiments/{item.Id}", new { name = "X", expectedVersion = 1 }), HttpStatusCode.Forbidden);
    }

    private CreateInstrumentRequest InstrumentRequest(string serial) => new()
    {
        OwnerId = fixture.OwnerId, Name = "Прилад", SerialNumber = serial, Unit = "m", MinValue = 0, MaxValue = 100, AbsoluteError = 0.1,
    };

    private Task<HttpResponseMessage> PostJsonAsync(string path, string body) =>
        Client.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"));

    internal static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal((int)status, json.RootElement.GetProperty("status").GetInt32());
        }
    }
}
