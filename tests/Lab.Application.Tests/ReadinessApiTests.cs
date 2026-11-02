using System.Net;
using Microsoft.EntityFrameworkCore;

namespace Lab.Application.Tests;

public sealed class ReadinessApiTests
{
    [Fact]
    public async Task Readiness_checks_schema_without_exposing_data_or_bypassing_resource_authentication()
    {
        await using var fixture = new ResourceApiFixture();
        await fixture.InitializeAsync();
        fixture.Client.DefaultRequestHeaders.Authorization = null;
        using var ready = await fixture.Client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Healthy", await ready.Content.ReadAsStringAsync());
        using var resource = await fixture.Client.GetAsync("/api/experiments");
        Assert.Equal(HttpStatusCode.Unauthorized, resource.StatusCode);
        await using var db = fixture.CreateContext();
        await db.Database.EnsureDeletedAsync();
        using var failed = await fixture.Client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal("Unhealthy", await failed.Content.ReadAsStringAsync());
    }
}
