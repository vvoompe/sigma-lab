using Lab.Application.Statistics;

namespace Lab.Application.Tests;

/// <summary>
/// Звірка розрахункового ядра з еталонними числами з docs/statistics-reference.md.
/// Числа в документі отримані незалежною реалізацією, тому тест ловить розходження,
/// а не підтверджує сам себе.
/// </summary>
public sealed class ReferenceSeriesTests
{
    private const double Tolerance = 1e-9;

    private static readonly double[] Homogeneous =
        [12.1, 12.4, 11.9, 12.0, 12.6, 12.2, 11.8, 12.3, 12.5, 12.15, 12.35];

    [Fact]
    public void Серія_A_дає_еталонні_середнє_СКО_та_похибку()
    {
        var result = SeriesStatistics.Calculate(Homogeneous, "°C");

        Assert.Equal(11, result.Count);
        Assert.Equal("°C", result.Unit);
        Assert.Equal(12.209090909090909, result.Mean, Tolerance);
        Assert.Equal(0.249818115654351, result.SampleStandardDeviation, Tolerance);
        Assert.Equal(0.075322995950827, result.StandardError, Tolerance);
    }

    [Fact]
    public void Серія_A_дає_еталонний_довірчий_інтервал()
    {
        var interval = SeriesStatistics.ConfidenceInterval(Homogeneous, "°C");

        Assert.Equal(0.95, interval.ConfidenceLevel);
        Assert.Equal("°C", interval.Unit);
        Assert.Equal(12.041260815364867, interval.Lower, Tolerance);
        Assert.Equal(12.376921002816951, interval.Upper, Tolerance);
    }

    [Fact]
    public void Серія_B_позначає_промах_і_не_торкається_даних()
    {
        var values = Homogeneous.Append(15.0).ToArray();

        var outlier = Assert.Single(MeasurementAnalysis.FindOutliers(values, "°C"));

        Assert.Equal(11, outlier.Index);
        Assert.Equal(15.0, outlier.Value);
        Assert.Equal(12, values.Length);
    }

    [Fact]
    public void Менший_промах_правилом_трьох_сигм_не_позначається()
    {
        // Межа правила задокументована: на короткій серії промах роздує СКО разом із порогом.
        var values = Homogeneous.Append(14.5).ToArray();

        Assert.Empty(MeasurementAnalysis.FindOutliers(values, "°C"));
    }

    [Fact]
    public void Регресія_дає_еталонні_коефіцієнти_та_прогноз()
    {
        double[] minutes = [0, 1, 2, 3, 4, 5, 6, 7];
        double[] temperature = [20.0, 20.6, 21.1, 21.9, 22.4, 23.0, 23.7, 24.3];

        var fit = LinearRegression.Fit(minutes, temperature, "хв", "°C");

        Assert.Equal(0.616666666666667, fit.Slope, Tolerance);
        Assert.Equal(19.966666666666669, fit.Intercept, Tolerance);
        Assert.Equal(0.998541210795040, fit.RSquared, Tolerance);
        Assert.Equal(0.054006172486731, fit.RootMeanSquaredResidual, Tolerance);
        Assert.Equal(26.133333333333333, fit.Predict(10), Tolerance);
    }

    [Fact]
    public void Порівняння_з_еталоном_використовує_включний_допуск()
    {
        var statistics = SeriesStatistics.Calculate(Homogeneous, "°C");

        var inside = MeasurementAnalysis.CompareToReference(statistics.Mean, "°C", 12.2, "°C", 0.5);
        Assert.True(inside.WithinTolerance);
        Assert.Equal(0.009090909090909, inside.AbsoluteDeviation, Tolerance);

        var outside = MeasurementAnalysis.CompareToReference(statistics.Mean, "°C", 12.2, "°C", 0.005);
        Assert.False(outside.WithinTolerance);
    }
}
