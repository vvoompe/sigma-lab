using System.Data.Common;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Lab.Application.Tests;

public sealed class ExperimentExportAccessTests
{
    [Fact]
    public Task SQLite_відкликання_менеджера_між_читаннями_забороняє_CSV() => VerifyAsync(null, revoke: true);

    [ResourcePostgresFact]
    public Task PostgreSQL_відкликання_менеджера_між_читаннями_забороняє_CSV() => VerifyAsync(PostgresConnection(), revoke: true);

    [Fact]
    public Task SQLite_паралельна_зміна_доступної_серії_лишається_конфліктом() => VerifyAsync(null, revoke: false);

    [ResourcePostgresFact]
    public Task PostgreSQL_паралельна_зміна_доступної_серії_лишається_конфліктом() => VerifyAsync(PostgresConnection(), revoke: false);

    private static async Task VerifyAsync(string? postgres, bool revoke)
    {
        var interceptor = new BeforeExportRead();
        await using var fixture = new ResourceApiFixture(postgres, queryInterceptor: interceptor);
        await fixture.InitializeAsync();
        var series = await fixture.CreateExperimentAsync();
        var added = await fixture.AddAsync(series.Id, series.Version, 12);
        await using (var db = fixture.CreateContext())
        {
            (await db.Sites.SingleAsync()).AssignManager(fixture.OtherId);
            await db.SaveChangesAsync();
        }
        fixture.Authenticate("LabManager", fixture.OtherId);
        using (var allowed = await fixture.Client.GetAsync($"/api/experiments/{series.Id}/export.csv"))
        {
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            Assert.Equal("text/csv", allowed.Content.Headers.ContentType?.MediaType);
        }
        var changed = false;
        // Перший авторизований SELECT завершений; фінальний SELECT ще не виконаний.
        interceptor.Callback = async () =>
        {
            await using var concurrent = fixture.CreateContext();
            if (revoke)
                (await concurrent.Sites.SingleAsync()).AssignManager(fixture.OwnerId);
            else
                (await concurrent.Experiments.SingleAsync()).Rename("Змінена паралельно серія");
            await concurrent.SaveChangesAsync();
            changed = true;
        };
        using var response = await fixture.Client.GetAsync($"/api/experiments/{series.Id}/export.csv");
        Assert.True(changed);
        Assert.Equal(revoke ? HttpStatusCode.NotFound : HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Content.Headers.ContentDisposition);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ExperimentId,ExperimentName", body, StringComparison.Ordinal);
        Assert.DoesNotContain(added.Measurement.ClientId.ToString(), body, StringComparison.Ordinal);
        await using var stored = fixture.CreateContext();
        Assert.Equal(revoke ? added.ExperimentVersion : added.ExperimentVersion + 1,
            (await stored.Experiments.SingleAsync()).Version);
        Assert.Single(await stored.Measurements.ToListAsync());
        Assert.Empty(await stored.AuditEntries.ToListAsync());
    }

    private static string PostgresConnection() => new NpgsqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("LAB_TEST_POSTGRES_CONNECTION")
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
        ?? throw new InvalidOperationException("Задайте тестове підключення PostgreSQL."))
        { Database = "resource_api_export_" + Guid.NewGuid().ToString("N") }.ConnectionString;

    private sealed class BeforeExportRead : DbCommandInterceptor
    {
        internal Func<Task>? Callback { get; set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Callback is { } callback &&
                (command.CommandText.Contains("JOIN \"measurements\"", StringComparison.Ordinal) ||
                 command.CommandText.Contains("JOIN measurements ", StringComparison.Ordinal)))
            {
                Callback = null;
                await callback();
            }
            return result;
        }
    }
}
