using Lab.Application.Statistics;

namespace Lab.Application.Tests;

public sealed class LinearRegressionTests
{
    [Fact]
    public void Точна_пряма_дає_відомі_коефіцієнти_і_прогноз()
    {
        var result = LinearRegression.Fit([1, 2, 3, 4], [3, 5, 7, 9], "с", "м");
        Assert.Equal(2.5, result.XMean);
        Assert.Equal(6, result.YMean);
        Assert.Equal(2, result.Slope, 12);
        Assert.Equal(1, result.Intercept, 12);
        Assert.Equal(1, result.RSquared, 12);
        Assert.Equal(0, result.RootMeanSquaredResidual, 12);
        Assert.Equal(11, result.Predict(5), 12);
        Assert.Equal("с", result.XUnit);
        Assert.Equal("м", result.YUnit);
    }

    [Fact]
    public void МНК_апроксимація_збігається_з_ручним_еталоном()
    {
        // xMean=1, yMean=4/3, Sxx=2, Sxy=2, SSE=2/3, Syy=8/3.
        var result = LinearRegression.Fit([0, 1, 2], [0, 2, 2], "с", "м");
        Assert.Equal(1, result.Slope, 12);
        Assert.Equal(1d / 3, result.Intercept, 12);
        Assert.Equal(0.75, result.RSquared, 12);
        Assert.Equal(Math.Sqrt(2d / 9), result.RootMeanSquaredResidual, 12);
        Assert.Equal(4d / 3, result.Predict(1), 12);
    }

    [Fact]
    public void Центрування_захищає_прогноз_на_великому_фоні()
    {
        var result = LinearRegression.Fit([1e12, 1e12 + 1, 1e12 + 2], [1, 3, 5], "с", "м");
        Assert.Equal(2, result.Slope, 12);
        Assert.Equal(4, result.Predict(1e12 + 1.5), 12);
    }

    [Fact]
    public void Стала_Y_дає_горизонтальну_пряму_з_умовним_R2_один()
    {
        var result = LinearRegression.Fit([0, 1, 2], [4, 4, 4], "с", "м");
        Assert.Equal(0, result.Slope);
        Assert.Equal(4, result.Intercept);
        Assert.Equal(1, result.RSquared);
        Assert.Equal(0, result.RootMeanSquaredResidual);
    }

    [Theory]
    [MemberData(nameof(InvalidSamples))]
    public void Некоректні_або_вироджені_входи_відхиляються(double[] x, double[] y) =>
        Assert.Throws<ArgumentException>(() => LinearRegression.Fit(x, y, "с", "м"));

    public static TheoryData<double[], double[]> InvalidSamples => new()
    {
        { Array.Empty<double>(), Array.Empty<double>() },
        { new[] { 1d }, new[] { 2d } },
        { new[] { 1d, 2d }, new[] { 1d, 2d, 3d } },
        { new[] { 1d, 1d }, new[] { 1d, 2d } },
        { new[] { double.NaN, 1d }, new[] { 1d, 2d } },
        { new[] { 0d, 1d }, new[] { 1d, double.PositiveInfinity } },
    };

    [Fact]
    public void Прогноз_точний_коли_середнє_X_не_представляється_double()
    {
        var result = LinearRegression.Fit([1e16, 1e16 + 2], [0, 2], "с", "м");
        Assert.Equal(0, result.Predict(1e16));
        Assert.Equal(2, result.Predict(1e16 + 2));
    }

    [Fact]
    public void Underflow_розкиду_Y_не_імітує_ідеальну_регресію() =>
        Assert.Throws<OverflowException>(() => LinearRegression.Fit([0, 1, 2], [0, 1e-200, 0], "с", "м"));

    [Fact]
    public void Дуже_малий_ненульовий_розкид_X_є_чисельною_помилкою() =>
        Assert.Throws<OverflowException>(() => LinearRegression.Fit([0, 1e-200], [0, 1], "с", "м"));

    [Fact]
    public void Дуже_великий_розмах_регресії_явно_відхиляється() =>
        Assert.Throws<OverflowException>(() => LinearRegression.Fit([0, 1e160], [0, 1e160], "с", "м"));

    [Fact]
    public void Відємний_нахил_і_зміщення_Y_коректні()
    {
        var result = LinearRegression.Fit([0, 1, 2], [1e16 + 4, 1e16 + 2, 1e16], "с", "м");
        Assert.Equal(-2, result.Slope);
        Assert.Equal(1e16 + 2, result.Predict(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => result.Predict(double.PositiveInfinity));
    }

    [Fact]
    public void Прогноз_не_приймає_NaN_і_переповнення()
    {
        var result = LinearRegression.Fit([0, 1], [0, 2], "с", "м");
        Assert.Throws<ArgumentOutOfRangeException>(() => result.Predict(double.NaN));
        Assert.Throws<OverflowException>(() => result.Predict(double.MaxValue));
    }
}
