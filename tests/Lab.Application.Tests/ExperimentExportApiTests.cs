using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Lab.Application.Tests;

public sealed class ExperimentExportApiTests
{
    [Fact]
    public async Task Export_has_utf8_bom_quoted_unicode_invariant_numbers_and_download_header()
    {
        await using var fixture = new ResourceApiFixture();
        await fixture.InitializeAsync();
        var series = await fixture.CreateExperimentAsync();
        using var renamed = await fixture.Client.PutAsJsonAsync($"/api/experiments/{series.Id}", new { name = "Польова, \"Київ\"", expectedVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        await fixture.AddAsync(series.Id, 2, 2.5);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("uk-UA");
            using var response = await fixture.Client.GetAsync($"/api/experiments/{series.Id}/export.csv");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
            Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.True(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
            var csv = Encoding.UTF8.GetString(bytes);
            Assert.Contains("\"Польова, \"\"Київ\"\"\"", csv, StringComparison.Ordinal);
            Assert.Contains(",2.5,\"m\",", csv, StringComparison.Ordinal);
            Assert.Contains("Z\r\n", csv, StringComparison.Ordinal);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("=HYPERLINK(1)")]
    [InlineData("+SUM(1)")]
    [InlineData("-1+2")]
    [InlineData("@SUM(1)")]
    public async Task Text_cells_cannot_be_interpreted_as_spreadsheet_formulas(string name)
    {
        await using var fixture = new ResourceApiFixture();
        await fixture.InitializeAsync();
        var series = await fixture.CreateExperimentAsync();
        using var renamed = await fixture.Client.PutAsJsonAsync($"/api/experiments/{series.Id}", new { name, expectedVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        await fixture.AddAsync(series.Id, 2, 1);
        var csv = await fixture.Client.GetStringAsync($"/api/experiments/{series.Id}/export.csv");
        Assert.Contains("\"'" + name + "\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_series_exports_header_and_foreign_or_anonymous_users_get_no_file()
    {
        await using var fixture = new ResourceApiFixture();
        await fixture.InitializeAsync();
        var series = await fixture.CreateExperimentAsync();
        var csv = await fixture.Client.GetStringAsync($"/api/experiments/{series.Id}/export.csv");
        Assert.Single(csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
        fixture.Authenticate(userId: fixture.OtherId);
        using var foreign = await fixture.Client.GetAsync($"/api/experiments/{series.Id}/export.csv");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        fixture.Client.DefaultRequestHeaders.Authorization = null;
        using var anonymous = await fixture.Client.GetAsync($"/api/experiments/{series.Id}/export.csv");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Signed_series_is_readable_and_openapi_describes_csv()
    {
        await using var fixture = new ResourceApiFixture();
        await fixture.InitializeAsync();
        var series = await fixture.CreateExperimentAsync();
        await fixture.AddAsync(series.Id, 1, 1);
        await using (var db = fixture.CreateContext())
        {
            (await db.Experiments.SingleAsync()).Sign(fixture.OtherId, DateTime.UtcNow);
            await db.SaveChangesAsync();
        }
        using var response = await fixture.Client.GetAsync($"/api/experiments/{series.Id}/export.csv");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var schema = JsonDocument.Parse(await fixture.Client.GetStringAsync("/openapi/v1.json"));
        Assert.True(schema.RootElement.GetProperty("paths").GetProperty("/api/experiments/{id}/export.csv")
            .GetProperty("get").GetProperty("responses").GetProperty("200").GetProperty("content").TryGetProperty("text/csv", out _));
    }
}
