using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Lab.Application.Abstractions;
using Lab.Application.DependencyInjection;
using Lab.Application.Resources;
using Lab.Application.Security;
using Lab.Contracts.Experiments;
using Lab.Contracts.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Lab.Application.Tests;

public sealed class MeasurementSyncRegressionTests
{
    [Fact]
    public Task SQLite_помилка_читання_версії_не_перериває_пакет() => VerifyItemFailureAsync(null);

    [ResourcePostgresFact]
    public Task PostgreSQL_помилка_читання_версії_не_перериває_пакет() => VerifyItemFailureAsync(PostgresConnection());

    [Fact]
    public Task SQLite_паралельний_дубль_повертає_узгоджену_версію() => VerifyDuplicateSnapshotAsync(null);

    [ResourcePostgresFact]
    public Task PostgreSQL_паралельний_дубль_повертає_узгоджену_версію() => VerifyDuplicateSnapshotAsync(PostgresConnection());

    [Fact]
    public async Task Скасування_під_час_відновлення_не_перетворюється_на_помилку_елемента()
    {
        var interceptor = new BeforeSyncQuery();
        await using var fixture = new ResourceApiFixture(queryInterceptor: interceptor);
        await fixture.InitializeAsync();
        var series = await fixture.CreateExperimentAsync();
        using var cancellation = new CancellationTokenSource();
        var conflictRaised = false;
        var versionRead = false;
        interceptor.Callback = command =>
        {
            if (!conflictRaised && command.CommandText.Contains("FROM \"measurements\"", StringComparison.Ordinal))
            {
                conflictRaised = true;
                throw new ResourceException(ResourceErrorKind.Conflict, "Конкурентна зміна.");
            }
            var firstLine = command.CommandText.Split('\n')[0];
            if (!firstLine.Contains(',', StringComparison.Ordinal) && firstLine.Contains("\"Version\"", StringComparison.Ordinal))
            {
                interceptor.Callback = null;
                versionRead = true;
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
            return Task.CompletedTask;
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLabApplication();
        services.AddSingleton<ICurrentUser>(new Researcher(fixture.OwnerId));
        services.AddScoped<IAppDbContext>(_ => fixture.CreateContext());
        await using var provider = services.BuildServiceProvider();
        var sync = provider.GetRequiredService<IMeasurementSyncService>();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sync.SynchronizeBatchAsync(
            new MeasurementSyncBatchRequest { Items = [Item(series.Id, 1), Item(series.Id, 1)] }, cancellation.Token));
        Assert.True(versionRead);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        await using var stored = fixture.CreateContext();
        Assert.Empty(await stored.Measurements.ToListAsync());
    }

    private static async Task VerifyItemFailureAsync(string? connection)
    {
        var interceptor = new BeforeSyncQuery();
        await using var fixture = new ResourceApiFixture(connection, queryInterceptor: interceptor);
        await fixture.InitializeAsync();
        var series = await fixture.CreateExperimentAsync();
        var readFailed = false;
        fixture.BeforeSave.Callback = () =>
        {
            // Падає саме додатковий SELECT Version після обробки конфлікту.
            interceptor.Callback = command =>
            {
                var firstLine = command.CommandText.Split('\n')[0];
                if (!firstLine.Contains(',', StringComparison.Ordinal) && firstLine.Contains("\"Version\"", StringComparison.Ordinal))
                {
                    interceptor.Callback = null;
                    readFailed = true;
                    throw new NpgsqlException("internal-version-query-detail");
                }
                return Task.CompletedTask;
            };
            throw new ResourceException(ResourceErrorKind.Conflict, "internal-conflict-detail");
        };
        var first = Item(series.Id, series.Version);
        var second = Item(series.Id, series.Version);
        using var response = await fixture.Client.PostAsJsonAsync("/api/sync/measurements",
            new MeasurementSyncBatchRequest { Items = [first, second] });
        Assert.True(readFailed);
        Assert.Equal(HttpStatusCode.MultiStatus, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<MeasurementSyncBatchResult>();
        Assert.NotNull(result);
        Assert.Equal([500, 201], result.Items.Select(item => item.Status));
        Assert.Equal([first.ClientId, second.ClientId], result.Items.Select(item => item.ClientId));
        Assert.Null(result.Items[0].Measurement);
        Assert.Null(result.Items[0].ExperimentVersion);
        Assert.Equal("Внутрішня помилка сервера.", result.Items[0].Error?.Title);
        Assert.DoesNotContain("internal-", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var stored = fixture.CreateContext();
        Assert.Equal(second.ClientId, (await stored.Measurements.SingleAsync()).ClientId);
        Assert.Equal(2, (await stored.Experiments.SingleAsync()).Version);
    }

    private static async Task VerifyDuplicateSnapshotAsync(string? connection)
    {
        var interceptor = new BeforeSyncQuery();
        await using var fixture = new ResourceApiFixture(connection, queryInterceptor: interceptor);
        await fixture.InitializeAsync();
        var series = await fixture.CreateExperimentAsync();
        var request = Item(series.Id, series.Version);
        MeasurementMutationDto? inserted = null;
        interceptor.Callback = async command =>
        {
            if (!command.CommandText.Contains("FROM \"measurements\"", StringComparison.Ordinal) &&
                !command.CommandText.Contains("FROM measurements ", StringComparison.Ordinal)) return;
            interceptor.Callback = null;
            using var created = await fixture.Client.PostAsJsonAsync($"/api/experiments/{series.Id}/measurements", new
            {
                request.ClientId, request.Value, request.MeasuredAt, request.ExpectedVersion,
            });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            inserted = await created.Content.ReadFromJsonAsync<MeasurementMutationDto>();
        };
        using var response = await fixture.Client.PostAsJsonAsync("/api/sync/measurements/single", request);
        Assert.NotNull(inserted);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var duplicate = await response.Content.ReadFromJsonAsync<MeasurementSyncResult>();
        Assert.NotNull(duplicate);
        Assert.True(duplicate.Duplicate);
        Assert.Equal(inserted.Measurement, duplicate.Measurement);
        Assert.Equal(2, duplicate.ExperimentVersion);
        Assert.Equal(inserted.ExperimentVersion, duplicate.ExperimentVersion);
        await using var stored = fixture.CreateContext();
        Assert.Single(await stored.Measurements.ToListAsync());
    }

    private static MeasurementSyncRequest Item(int id, long version) => new()
    {
        ExperimentId = id, ExpectedVersion = version, ClientId = Guid.NewGuid(), Value = 2.5,
        MeasuredAt = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc),
    };

    private static string PostgresConnection() => new NpgsqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("LAB_TEST_POSTGRES_CONNECTION")
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
        ?? throw new InvalidOperationException("Задайте тестове підключення PostgreSQL."))
        { Database = "resource_api_" + Guid.NewGuid().ToString("N") }.ConnectionString;

    private sealed class BeforeSyncQuery : DbCommandInterceptor
    {
        internal Func<DbCommand, Task>? Callback { get; set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Callback is { } callback) await callback(command);
            return result;
        }
    }

    private sealed class Researcher(int id) : ICurrentUser
    {
        public int? Id => id;
        public string? Role => "Researcher";
    }
}
