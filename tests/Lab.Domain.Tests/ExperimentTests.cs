using Lab.Domain.Experiments;
using Lab.Domain.Instruments;
using Lab.Domain.ReferenceValues;
using Lab.Domain.Users;

namespace Lab.Domain.Tests;

public sealed class ExperimentTests
{
    [Fact]
    public void Профіль_не_містить_логіки_авторизації()
    {
        var user = new User("  Дослідник  ", " Test@Example.org ");
        Assert.Equal("Дослідник", user.Name);
        Assert.Equal("Test@Example.org", user.Email);
        Assert.Equal("TEST@EXAMPLE.ORG", user.NormalizedEmail);
    }

    [Fact]
    public void Серія_додає_вимір_одного_приладу_й_одиниці()
    {
        var instrument = new Instrument(1, "Прилад", "SN-1", "°C", -10, 50, 0.1);
        var series = new Experiment(1, 1, "Серія", instrument);
        var clientId = Guid.NewGuid();
        var time = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
        var measurement = series.AddMeasurement(clientId, 20, "°C", time);
        Assert.Same(measurement, Assert.Single(series.Measurements));
        Assert.Equal(clientId, measurement.ClientId);
        Assert.Equal(20, measurement.Value);
        Assert.Equal("°C", series.Unit);
        Assert.Equal(time, measurement.MeasuredAt);
        Assert.Equal(2, series.Version);
    }

    [Theory]
    [InlineData(-11, "°C")]
    [InlineData(51, "°C")]
    [InlineData(double.NaN, "°C")]
    [InlineData(double.PositiveInfinity, "°C")]
    [InlineData(20, "м")]
    public void Невірні_значення_не_потрапляють_у_серію(double value, string unit)
    {
        var series = CreateSeries();
        Assert.ThrowsAny<ArgumentException>(() => series.AddMeasurement(Guid.NewGuid(), value, unit, DateTime.UtcNow));
        Assert.Empty(series.Measurements);
        Assert.Equal(1, series.Version);
    }

    [Fact]
    public void Підписана_серія_не_дозволяє_жодних_змін_вимірів()
    {
        var series = CreateSeries();
        var measurement = series.AddMeasurement(Guid.NewGuid(), 20, "°C", DateTime.UtcNow);
        var signedAt = DateTime.UtcNow;
        series.Sign(2, signedAt);
        Assert.Equal(ExperimentStatus.SignedOff, series.Status);
        Assert.Equal(signedAt, series.SignedAt);
        Assert.Equal(2, series.SignedById);
        Assert.Throws<InvalidOperationException>(() => series.AddMeasurement(Guid.NewGuid(), 10, "°C", DateTime.UtcNow));
        Assert.Throws<InvalidOperationException>(() => measurement.Update(10, "°C", DateTime.UtcNow));
        Assert.Throws<InvalidOperationException>(() => series.RemoveMeasurement(measurement));
        Assert.Throws<InvalidOperationException>(() => series.Sign(2, DateTime.UtcNow));
        Assert.Equal(20, measurement.Value);
    }

    [Fact]
    public void Чернетку_можна_редагувати_та_очистити()
    {
        var series = CreateSeries();
        var measurement = series.AddMeasurement(Guid.NewGuid(), 20, "°C", DateTime.UtcNow);
        measurement.Update(25, "°C", DateTime.UtcNow);
        Assert.Equal(25, measurement.Value);
        Assert.Equal(2, measurement.Version);
        series.RemoveMeasurement(measurement);
        Assert.Empty(series.Measurements);
        Assert.Equal(4, series.Version);
    }

    [Fact]
    public void Порожній_ClientId_та_не_UTC_відхиляються()
    {
        var series = CreateSeries();
        Assert.Throws<ArgumentException>(() => series.AddMeasurement(Guid.Empty, 20, "°C", DateTime.UtcNow));
        Assert.Throws<ArgumentException>(() => series.AddMeasurement(Guid.NewGuid(), 20, "°C", DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void Локальний_дубль_ClientId_відхиляється()
    {
        var series = CreateSeries();
        var id = Guid.NewGuid();
        series.AddMeasurement(id, 20, "°C", DateTime.UtcNow);
        Assert.Throws<InvalidOperationException>(() => series.AddMeasurement(id, 21, "°C", DateTime.UtcNow));
    }

    [Fact]
    public void Еталон_мусить_мати_одиницю_приладу()
    {
        var instrument = new Instrument(1, "Прилад", "SN-1", "°C", -10, 50, 0.1);
        var reference = new ReferenceValue("Еталон", "м", 20, 0.1);
        Assert.Throws<ArgumentException>(() => new Experiment(1, 1, "Серія", instrument, reference));
    }

    private static Experiment CreateSeries() => new(1, 1, "Серія", new Instrument(1, "Прилад", "SN-1", "°C", -10, 50, 0.1));
}
