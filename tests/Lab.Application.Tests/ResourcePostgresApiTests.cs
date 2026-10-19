using System.Net;
using System.Net.Http.Json;
using Lab.Contracts.Experiments;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lab.Application.Tests;

public sealed class ResourcePostgresApiTests
{
    [ResourcePostgresFact]
    public async Task PostgreSQL_HTTP_series_statistics_and_concurrency_rollback()
    {
        await using var fixture = new ResourceApiFixture(Connection());
        await fixture.InitializeAsync();
        var item = await fixture.CreateExperimentAsync(reference: true);
        await fixture.AddAsync(item.Id, 1, 1);
        await fixture.AddAsync(item.Id, 2, 2);
        await fixture.AddAsync(item.Id, 3, 3);
        var stats = await fixture.Client.GetFromJsonAsync<ExperimentStatisticsDto>($"/api/experiments/{item.Id}/statistics");
        Assert.NotNull(stats);
        Assert.Equal(2, stats.Mean);
        Assert.Equal(1, stats.SampleStandardDeviation);
        Assert.Equal("m", stats.Unit);
        fixture.BeforeSave.Callback = async () =>
        {
            await using var competing = fixture.CreateContext();
            var experiment = await competing.Experiments.SingleAsync();
            experiment.Sign(fixture.OtherId, DateTime.UtcNow);
            await competing.SaveChangesAsync();
        };
        await ResourceApiTests.AssertProblemAsync(await fixture.Client.DeleteAsync($"/api/experiments/{item.Id}?expectedVersion=4"), HttpStatusCode.Conflict);
        await using var stored = fixture.CreateContext();
        Assert.Empty(await stored.AuditEntries.ToListAsync());
        Assert.Equal(3, await stored.Measurements.CountAsync());
        await ResourceApiTests.AssertProblemAsync(await fixture.Client.PostAsJsonAsync($"/api/experiments/{item.Id}/measurements", ResourceApiFixture.Measurement(5, 4)), HttpStatusCode.Conflict);
    }

    [ResourcePostgresFact]
    public async Task PostgreSQL_UUID_race_does_not_leak_foreign_payload()
    {
        await using var fixture = new ResourceApiFixture(Connection());
        await fixture.InitializeAsync();
        var first = await fixture.CreateExperimentAsync();
        var second = await fixture.CreateExperimentAsync();
        var request = ResourceApiFixture.Measurement(1, 5);
        fixture.BeforeSave.Callback = async () =>
        {
            await using var competing = fixture.CreateContext();
            var experiment = await competing.Experiments.Include(x => x.Instrument).Include(x => x.Measurements)
                .SingleAsync(x => x.Id == second.Id);
            experiment.AddMeasurement(request.ClientId.GetValueOrDefault(), 99, experiment.Unit, DateTime.UtcNow);
            await competing.SaveChangesAsync();
        };
        using var response = await fixture.Client.PostAsJsonAsync($"/api/experiments/{first.Id}/measurements", request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.DoesNotContain("99", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var db = fixture.CreateContext();
        Assert.Equal(1, (await db.Experiments.SingleAsync(x => x.Id == first.Id)).Version);
        Assert.Equal(second.Id, (await db.Measurements.SingleAsync()).ExperimentId);
    }

    private static string Connection()
    {
        var basis = Environment.GetEnvironmentVariable("LAB_TEST_POSTGRES_CONNECTION")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? throw new InvalidOperationException("Для HTTP-тестів PostgreSQL задайте явне підключення.");
        return new NpgsqlConnectionStringBuilder(basis) { Database = "resource_api_" + Guid.NewGuid().ToString("N") }.ConnectionString;
    }
}

public sealed class ResourcePostgresFactAttribute : FactAttribute
{
    public ResourcePostgresFactAttribute()
    {
        var enabled = Environment.GetEnvironmentVariable("LAB_TEST_POSTGRES");
        if (enabled != "1" && !string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
            Skip = "Потрібен LAB_TEST_POSTGRES=1 та явне підключення PostgreSQL.";
    }
}
