using Lab.Domain.Experiments;
using Lab.Domain.Instruments;
using Lab.Domain.ReferenceValues;
using Lab.Domain.Users;

namespace Lab.Domain.Tests;

public sealed class ModelValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("Name <test@example.org>")]
    public void Профіль_відхиляє_некоректний_email(string email) =>
        Assert.ThrowsAny<ArgumentException>(() => new User("Дослідник", email));

    [Fact]
    public void Профіль_відхиляє_порожнє_та_надто_довге_імя()
    {
        Assert.Throws<ArgumentException>(() => new User(" ", "test@example.org"));
        Assert.Throws<ArgumentException>(() => new User(new string('a', 101), "test@example.org"));

    }

    [Theory]
    [InlineData(0, 10, -0.1)]
    [InlineData(10, 10, 0)]
    [InlineData(11, 10, 0)]
    [InlineData(double.NaN, 10, 0)]
    [InlineData(0, double.PositiveInfinity, 0)]
    [InlineData(0, 10, double.NegativeInfinity)]
    public void Прилад_валідує_межі_та_похибку(double minimum, double maximum, double error) =>
        Assert.ThrowsAny<ArgumentException>(() => new Instrument(1, "Прилад", "SN", "м", minimum, maximum, error));

    [Fact]
    public void Прилад_валідує_власника_назву_номер_та_одиницю()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Instrument(0, "Прилад", "SN", "м", 0, 10, 0));
        Assert.Throws<ArgumentException>(() => new Instrument(1, " ", "SN", "м", 0, 10, 0));
        Assert.Throws<ArgumentException>(() => new Instrument(1, "Прилад", " ", "м", 0, 10, 0));
        Assert.Throws<ArgumentException>(() => new Instrument(1, "Прилад", "SN", new string('x', 33), 0, 10, 0));
    }

    [Fact]
    public void Межі_приладу_включні_та_нульова_похибка_дозволена()
    {
        var instrument = new Instrument(1, " Прилад ", " SN ", " м ", -10, 10, 0);
        instrument.ValidateMeasurement(-10, "м");
        instrument.ValidateMeasurement(10, "м");
        Assert.Equal("SN", instrument.SerialNumber);
        Assert.Equal("м", instrument.Unit);
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(1, double.PositiveInfinity)]
    [InlineData(1, -1)]
    public void Еталон_валідує_значення_й_допуск(double value, double tolerance) =>
        Assert.ThrowsAny<ArgumentException>(() => new ReferenceValue("Еталон", "м", value, tolerance));

    [Fact]
    public void Серія_відхиляє_чужий_прилад_і_невалідні_посилання()
    {
        var instrument = new Instrument(1, "Прилад", "SN", "м", 0, 10, 0);
        Assert.Throws<ArgumentException>(() => new Experiment(1, 2, "Серія", instrument));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Experiment(0, 1, "Серія", instrument));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Experiment(1, 0, "Серія", instrument));

    }

    [Fact]
    public void Відхилені_зміни_не_псують_попередній_вимір()
    {
        var series = CreateSeries();
        var measurement = series.AddMeasurement(Guid.NewGuid(), 5, "м", DateTime.UtcNow);
        Assert.Throws<ArgumentOutOfRangeException>(() => measurement.Update(11, "м", DateTime.UtcNow));
        Assert.Throws<ArgumentException>(() => measurement.Update(5, "°C", DateTime.UtcNow));
        Assert.Throws<ArgumentException>(() => measurement.Update(5, "м", DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)));
        Assert.Equal(5, measurement.Value);
        Assert.Equal(1, measurement.Version);
        Assert.Equal(2, series.Version);
    }

    [Fact]
    public void Не_можна_видалити_чужий_вимір_або_підписати_без_автора_і_UTC()
    {
        var series = CreateSeries();
        var another = CreateSeries().AddMeasurement(Guid.NewGuid(), 5, "м", DateTime.UtcNow);
        Assert.Throws<ArgumentException>(() => series.RemoveMeasurement(another));

        Assert.Throws<ArgumentOutOfRangeException>(() => series.Sign(0, DateTime.UtcNow));
        Assert.Throws<ArgumentException>(() => series.Sign(1, DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)));
        Assert.Equal(ExperimentStatus.Draft, series.Status);
        Assert.Equal(1, series.Version);
    }

    [Fact]
    public void Перейменування_приладу_не_чіпає_метрологію()
    {
        var instrument = new Instrument(1, "Прилад", "SN-1", "м", -10, 10, 0.1);
        instrument.Rename("  Прилад 2  ");
        Assert.Equal("Прилад 2", instrument.Name);
        Assert.Equal("SN-1", instrument.SerialNumber);
        Assert.Equal("м", instrument.Unit);
        Assert.Equal(-10, instrument.MinValue);
        Assert.Equal(10, instrument.MaxValue);
        Assert.Equal(0.1, instrument.AbsoluteError);
    }

    [Fact]
    public void Перейменування_приладу_валідує_назву()
    {
        var instrument = new Instrument(1, "Прилад", "SN-1", "м", -10, 10, 0.1);
        Assert.Throws<ArgumentException>(() => instrument.Rename(" "));
        Assert.Throws<ArgumentException>(() => instrument.Rename(new string('a', 101)));
        Assert.Equal("Прилад", instrument.Name);
    }

    private static Experiment CreateSeries() => new(1, 1, "Серія", new Instrument(1, "Прилад", "SN", "м", 0, 10, 0));
}
