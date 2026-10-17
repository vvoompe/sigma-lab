using Lab.Application.Statistics;

namespace Lab.Application.Tests;

public sealed class MeasurementAnalysisTests
{
    [Fact]
    public void Правило_трьох_сигм_позначає_але_не_видаляє_викид()
    {
        var input = Enumerable.Repeat(0d, 19).Append(100).ToArray();
        var outliers = MeasurementAnalysis.FindOutliers(input, "м");
        var found = Assert.Single(outliers);
        Assert.Equal(19, found.Index);
        Assert.Equal(100, found.Value);
        Assert.Equal(20, input.Length);
    }

    [Fact]
    public void Поріг_викидів_не_змінюється_через_округлення_середнього()
    {
        // Центровані значення [0,2,4,6,2]: mean=2.8, s=sqrt(5.2), max z<1.5.
        Assert.Empty(MeasurementAnalysis.FindOutliers([1e16, 1e16 + 2, 1e16 + 4, 1e16 + 6, 1e16 + 2], "м", 1.5));
    }

    [Fact]
    public void Стала_й_мала_вибірки_не_мають_помилкових_викидів()
    {
        Assert.Empty(MeasurementAnalysis.FindOutliers([1, 1, 1], "м"));
        Assert.Empty(MeasurementAnalysis.FindOutliers([0, 100], "м"));
    }

    [Fact]
    public void Одиниці_порівняння_тримляться() =>
        Assert.True(MeasurementAnalysis.CompareToReference(1, " м ", 1, "м", 0).WithinTolerance);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.PositiveInfinity)]
    public void Неприпустимий_поріг_відхиляється(double threshold) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementAnalysis.FindOutliers([1, 2], "м", threshold));

    [Theory]
    [InlineData(9, 10, 1, true)]
    [InlineData(11, 10, 1, true)]
    [InlineData(12, 10, 1, false)]
    [InlineData(0, 0, 0, true)]
    [InlineData(-2, -1, 1, true)]
    public void Абсолютний_допуск_включає_межу(double value, double reference, double tolerance, bool expected)
    {
        var result = MeasurementAnalysis.CompareToReference(value, "м", reference, "м", tolerance);
        Assert.Equal(expected, result.WithinTolerance);
        Assert.Equal(Math.Abs(value - reference), result.AbsoluteDeviation);
        Assert.Equal("м", result.Unit);
        Assert.Equal(tolerance, result.AbsoluteTolerance);
    }

    [Fact]
    public void Різні_одиниці_не_порівнюються_мовчки() =>
        Assert.Throws<ArgumentException>(() => MeasurementAnalysis.CompareToReference(1, "м", 100, "см", 0));

    [Fact]
    public void Переповнення_відхилення_не_повертає_нескінченність() =>
        Assert.Throws<OverflowException>(() => MeasurementAnalysis.CompareToReference(double.MaxValue, "м", -double.MaxValue, "м", 1));

    [Fact]
    public void Порожня_одиниця_відхиляється()
    {
        Assert.Throws<ArgumentException>(() => MeasurementAnalysis.CompareToReference(1, " ", 1, "м", 1));
        Assert.Throws<ArgumentException>(() => MeasurementAnalysis.CompareToReference(1, "м", 1, " ", 1));
        Assert.Throws<ArgumentNullException>(() => MeasurementAnalysis.FindOutliers(null!, "м"));
    }

    [Fact]
    public void Користувацький_поріг_змінює_чутливість()
    {
        Assert.Equal(2, MeasurementAnalysis.FindOutliers([0, 2], "м", 0.5).Count);
        Assert.Empty(MeasurementAnalysis.FindOutliers([0, 2], "м", 1));
    }

    [Theory]
    [InlineData(double.NaN, 1, 1)]
    [InlineData(1, double.PositiveInfinity, 1)]
    [InlineData(1, 1, double.NaN)]
    [InlineData(1, 1, -1)]
    [InlineData(1, 1, double.PositiveInfinity)]
    public void Некоректні_аргументи_порівняння_відхиляються(double value, double reference, double tolerance) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementAnalysis.CompareToReference(value, "м", reference, "м", tolerance));
}
