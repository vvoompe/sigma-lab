using System.Net;
using System.Net.Http.Json;
using Lab.Domain.Experiments;
using Lab.Domain.Instruments;
using Microsoft.EntityFrameworkCore;

namespace Lab.Application.Tests;

public sealed class ResourceRaceApiTests
{
    [Theory]
    [InlineData("instrument-create")]
    [InlineData("instrument-delete")]
    [InlineData("experiment-create")]
    [InlineData("measurement-create")]
    public async Task Unknown_storage_failure_is_not_reported_as_conflict_or_exposed(string operation)
    {
        await using var fixture = new ResourceApiFixture();
        await fixture.InitializeAsync();
        var experimentId = operation == "measurement-create" ? (await fixture.CreateExperimentAsync()).Id : 0;
        if (operation.StartsWith("instrument", StringComparison.Ordinal)) fixture.Authenticate("Administrator");
        // Імітуємо збій сховища, не пов'язаний із відомими UUID/FK/Version-конфліктами.
        fixture.BeforeSave.Callback = () => throw new DbUpdateException("private-storage-diagnostic");
        using var response = operation switch
        {
            "instrument-create" => await fixture.Client.PostAsJsonAsync("/api/instruments", new
            {
                ownerId = fixture.OwnerId, name = "Прилад", serialNumber = "Failed", unit = "m",
                minValue = 0, maxValue = 100, absoluteError = 0.1,
            }),
            "instrument-delete" => await fixture.Client.DeleteAsync($"/api/instruments/{fixture.InstrumentId}"),
            "experiment-create" => await fixture.Client.PostAsJsonAsync("/api/experiments", new
            {
                name = "Серія", siteId = fixture.SiteId, instrumentId = fixture.InstrumentId,
                referenceValueId = fixture.ReferenceId,
            }),
            _ => await fixture.Client.PostAsJsonAsync($"/api/experiments/{experimentId}/measurements", ResourceApiFixture.Measurement(1, 5)),
        };
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("private-storage-diagnostic", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var db = fixture.CreateContext();
        Assert.Single(await db.Instruments.ToListAsync());
        Assert.Empty(await db.Measurements.ToListAsync());
        Assert.Empty(await db.AuditEntries.ToListAsync());
        Assert.Equal(experimentId == 0 ? 0 : 1, await db.Experiments.CountAsync());
    }

    [Fact]
    public async Task Concurrent_serial_creation_returns_409_and_keeps_only_winner()
    {
        await using var fixture = new ResourceApiFixture();
        await fixture.InitializeAsync();
        fixture.Authenticate("Administrator");
        fixture.BeforeSave.Callback = async () =>
        {
            await using var competing = fixture.CreateContext();
            competing.Instruments.Add(new Instrument(fixture.OwnerId, "Переможець", "Race-1", "m", 0, 100, 0.1));
            await competing.SaveChangesAsync();
        };
        await ResourceApiTests.AssertProblemAsync(await fixture.Client.PostAsJsonAsync("/api/instruments", new
        {
            ownerId = fixture.OwnerId, name = "Другий", serialNumber = "Race-1", unit = "m",
            minValue = 0, maxValue = 100, absoluteError = 0.1,
        }), HttpStatusCode.Conflict);
        await using var db = fixture.CreateContext();
        Assert.Equal("Переможець", (await db.Instruments.SingleAsync(x => x.SerialNumber == "Race-1")).Name);
    }

    [Fact]
    public async Task Concurrent_owner_deletion_returns_409_without_orphan_instrument()
    {
        await using var fixture = new ResourceApiFixture();
        await fixture.InitializeAsync();
        fixture.Authenticate("Administrator");
        fixture.BeforeSave.Callback = async () =>
        {
            await using var competing = fixture.CreateContext();
            competing.Users.Remove(await competing.Users.SingleAsync(x => x.Id == fixture.OtherId));
            await competing.SaveChangesAsync();
        };
        await ResourceApiTests.AssertProblemAsync(await fixture.Client.PostAsJsonAsync("/api/instruments", new
        {
            ownerId = fixture.OtherId, name = "Прилад", serialNumber = "Orphan-1", unit = "m",
            minValue = 0, maxValue = 100, absoluteError = 0.1,
        }), HttpStatusCode.Conflict);
        await using var db = fixture.CreateContext();
        Assert.False(await db.Instruments.AnyAsync(x => x.SerialNumber == "Orphan-1"));
    }

    [Fact]
    public async Task Concurrent_series_creation_prevents_instrument_deletion()
    {
        await using var fixture = new ResourceApiFixture();
        await fixture.InitializeAsync();
        fixture.Authenticate("Administrator");
        fixture.BeforeSave.Callback = async () =>
        {
            await using var competing = fixture.CreateContext();
            var instrument = await competing.Instruments.SingleAsync();
            competing.Experiments.Add(new Experiment(fixture.SiteId, fixture.OwnerId, "Переможець", instrument));
            await competing.SaveChangesAsync();
        };
        await ResourceApiTests.AssertProblemAsync(await fixture.Client.DeleteAsync($"/api/instruments/{fixture.InstrumentId}"), HttpStatusCode.Conflict);
        await using var db = fixture.CreateContext();
        Assert.Equal(fixture.InstrumentId, (await db.Experiments.SingleAsync()).InstrumentId);
        Assert.True(await db.Instruments.AnyAsync(x => x.Id == fixture.InstrumentId));
    }

    [Theory]
    [InlineData("site")]
    [InlineData("instrument")]
    [InlineData("reference")]
    public async Task Concurrent_parent_deletion_returns_409_without_partial_series(string parent)
    {
        await using var fixture = new ResourceApiFixture();
        await fixture.InitializeAsync();
        fixture.BeforeSave.Callback = async () =>
        {
            await using var competing = fixture.CreateContext();
            switch (parent)
            {
                case "site": competing.Sites.Remove(await competing.Sites.SingleAsync()); break;
                case "instrument": competing.Instruments.Remove(await competing.Instruments.SingleAsync()); break;
                case "reference": competing.ReferenceValues.Remove(await competing.ReferenceValues.SingleAsync()); break;
            }
            await competing.SaveChangesAsync();
        };
        await ResourceApiTests.AssertProblemAsync(await fixture.Client.PostAsJsonAsync("/api/experiments", new
        {
            name = "Серія", siteId = fixture.SiteId, instrumentId = fixture.InstrumentId,
            referenceValueId = fixture.ReferenceId,
        }), HttpStatusCode.Conflict);
        await using var db = fixture.CreateContext();
        Assert.Empty(await db.Experiments.ToListAsync());
    }
}
