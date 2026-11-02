using System.Net;
using System.Net.Http.Json;
using Lab.Contracts.Experiments;
using Lab.Contracts.ReferenceValues;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lab.Application.Tests;

/// <summary>Зміна одиниці не може зламати серію, яка вже прочитала еталон, але ще не збережена.</summary>
public sealed class ReferenceValueConcurrencyTests
{
    [Fact]
    public async Task SQLite_створення_серії_не_дозволяє_паралельно_змінити_одиницю()
    {
        var path = Path.Combine(Path.GetTempPath(), $"reference-race-{Guid.NewGuid():N}.db");
        try
        {
            await using var fixture = new ResourceApiFixture(sqliteConnectionString: $"Data Source={path};Pooling=False");
            await VerifyConcurrentCreationAsync(fixture);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + "-wal");
            File.Delete(path + "-shm");
        }
    }

    [ResourcePostgresFact]
    public async Task PostgreSQL_створення_серії_не_дозволяє_паралельно_змінити_одиницю()
    {
        var basis = Environment.GetEnvironmentVariable("LAB_TEST_POSTGRES_CONNECTION")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? throw new InvalidOperationException("Потрібне явне підключення PostgreSQL.");
        var connection = new NpgsqlConnectionStringBuilder(basis)
        {
            Database = "resource_api_" + Guid.NewGuid().ToString("N"),
        }.ConnectionString;
        await using var fixture = new ResourceApiFixture(connection);
        await VerifyConcurrentCreationAsync(fixture);
    }

    private static async Task VerifyConcurrentCreationAsync(ResourceApiFixture fixture)
    {
        await fixture.InitializeAsync();
        var creating = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.BeforeSave.Callback = async () =>
        {
            // Серія вже пройшла доменну перевірку m == m, але FK ще не записаний.
            creating.SetResult();
            await resume.Task.WaitAsync(TimeSpan.FromSeconds(20));
        };
        var pending = fixture.Client.PostAsJsonAsync("/api/experiments", new
        {
            name = "Конкурентна серія", siteId = fixture.SiteId,
            instrumentId = fixture.InstrumentId, referenceValueId = fixture.ReferenceId,
        });

        HttpResponseMessage changed;
        try
        {
            await creating.Task.WaitAsync(TimeSpan.FromSeconds(20));
            fixture.Authenticate("Administrator");
            changed = await fixture.Client.PutAsJsonAsync($"/api/reference-values/{fixture.ReferenceId}", new
            {
                name = "Несумісний еталон", unit = "cm", value = 300, absoluteTolerance = 100,
            });
        }
        finally
        {
            resume.TrySetResult();
        }

        using var created = await pending.WaitAsync(TimeSpan.FromSeconds(20));
        using (changed)
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        }
        var experiment = await created.Content.ReadFromJsonAsync<ExperimentDto>();
        Assert.NotNull(experiment);
        await using (var db = fixture.CreateContext())
        {
            var stored = await db.ReferenceValues.AsNoTracking().SingleAsync();
            Assert.Equal("Еталон", stored.Name);
            Assert.Equal("m", stored.Unit);
            Assert.Equal(3, stored.Value);
            Assert.Equal(1, stored.AbsoluteTolerance);
        }

        // Незмінність одиниці не блокує звичайне редагування решти полів.
        using var sameUnit = await fixture.Client.PutAsJsonAsync($"/api/reference-values/{fixture.ReferenceId}", new
        {
            name = "Еталон 2", unit = " m ", value = 4, absoluteTolerance = 2,
        });
        Assert.Equal(HttpStatusCode.OK, sameUnit.StatusCode);
        var reference = await sameUnit.Content.ReadFromJsonAsync<ReferenceValueDto>();
        Assert.NotNull(reference);
        Assert.Equal("m", reference.Unit);
        Assert.Equal("Еталон 2", reference.Name);
        Assert.Equal(4, reference.Value);
        Assert.Equal(2, reference.AbsoluteTolerance);

        fixture.Authenticate();
        await fixture.AddAsync(experiment.Id, 1, 3);
        await fixture.AddAsync(experiment.Id, 2, 5);
        var statistics = await fixture.Client.GetFromJsonAsync<ExperimentStatisticsDto>(
            $"/api/experiments/{experiment.Id}/statistics");
        Assert.NotNull(statistics);
        Assert.Equal("m", statistics.Unit);
        Assert.Equal(4, statistics.Mean);
    }
}
