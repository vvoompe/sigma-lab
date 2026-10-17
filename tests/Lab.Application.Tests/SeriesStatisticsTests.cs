using Lab.Application.Statistics;

namespace Lab.Application.Tests;

public sealed class SeriesStatisticsTests
{
    [Fact]
    public void Інтервал_Стьюдента_для_двох_вимірів_збігається_з_Коші()
    {
        // Для df=1: t(0.975)=tan(0.475*pi), SE([0,2])=1.
        var interval = SeriesStatistics.ConfidenceInterval([0, 2], "м", 0.95);
        var expectedMargin = Math.Tan(0.475 * Math.PI);
        Assert.Equal(0.95, interval.ConfidenceLevel);
        Assert.Equal("м", interval.Unit);
        Assert.Equal(1 - expectedMargin, interval.Lower, 7);
        Assert.Equal(1 + expectedMargin, interval.Upper, 7);
    }

    [Theory]
    [InlineData(0.90)]
    [InlineData(0.95)]
    [InlineData(0.99)]
    public void Нульова_дисперсія_дає_точковий_інтервал(double confidence)
    {
        var result = SeriesStatistics.ConfidenceInterval([5, 5, 5], "м", confidence);
        Assert.Equal(5, result.Lower);
        Assert.Equal(5, result.Upper);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-0.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Некоректний_рівень_довіри_відхиляється(double confidence) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SeriesStatistics.ConfidenceInterval([0, 2], "м", confidence));

    [Theory]
    [MemberData(nameof(InvalidSamples))]
    public void Недостатня_або_нескінченна_вибірка_відхиляється(double[] values) =>
        Assert.Throws<ArgumentException>(() => SeriesStatistics.Calculate(values, "м"));

    public static TheoryData<double[]> InvalidSamples => new()
    {
        Array.Empty<double>(), new[] { 1d }, new[] { 1d, double.NaN },
        new[] { double.NegativeInfinity, 1d }, new[] { 1d, double.PositiveInfinity },
    };

    [Fact]
    public void Великі_близькі_значення_не_втрачають_дисперсію()
    {
        var result = SeriesStatistics.Calculate([1e12 + 1, 1e12 + 2, 1e12 + 3], "м");
        Assert.Equal(1e12 + 2, result.Mean);
        Assert.Equal(1, result.SampleStandardDeviation);
    }

    [Fact]
    public void Втрата_ненульової_дисперсії_через_underflow_не_маскується_нулем() =>
        Assert.Throws<OverflowException>(() => SeriesStatistics.Calculate([0, 1e-200], "м"));

    [Fact]
    public void Переповнення_не_повертає_нескінченну_статистику() =>
        Assert.Throws<OverflowException>(() => SeriesStatistics.Calculate([-double.MaxValue, double.MaxValue], "м"));

    [Fact]
    public void Вхідний_масив_не_змінюється()
    {
        double[] input = [3, 1, 2];
        SeriesStatistics.Calculate(input, "м");
        Assert.Equal(new[] { 3d, 1d, 2d }, input);
    }

    [Fact]
    public void Null_вибірка_і_порожня_одиниця_відхиляються()
    {
        Assert.Throws<ArgumentNullException>(() => SeriesStatistics.Calculate(null!, "м"));
        Assert.Throws<ArgumentException>(() => SeriesStatistics.Calculate([1, 2], " "));
    }

    [Fact]
    public void Інтервал_для_df4_збігається_з_табличним_квантилем()
    {
        // t(4,0.975)=2.7764451051977987; для [1..5] SE=sqrt(0.5).
        var result = SeriesStatistics.ConfidenceInterval([1, 2, 3, 4, 5], "м");
        var margin = 2.7764451051977987 * Math.Sqrt(0.5);
        Assert.Equal(3 - margin, result.Lower, 7);
        Assert.Equal(3 + margin, result.Upper, 7);
    }

    [Fact]
    public void Більша_довіра_дає_ширший_інтервал()
    {
        var narrow = SeriesStatistics.ConfidenceInterval([1, 2, 3], "м", 0.9);
        var wide = SeriesStatistics.ConfidenceInterval([1, 2, 3], "м", 0.99);
        Assert.True(wide.Lower < narrow.Lower);
        Assert.True(wide.Upper > narrow.Upper);
    }

    [Fact]
    public void Дуже_високий_рівень_довіри_збігається_з_аналітичним_df1()
    {
        const double confidence = 1 - 1e-12;
        var result = SeriesStatistics.ConfidenceInterval([0, 2], "м", confidence);
        var expected = 1 / Math.Tan(Math.PI * (1 - confidence) / 2);
        Assert.True(Math.Abs((result.Upper - 1) / expected - 1) < 1e-6);
    }

    [Fact]
    public void Underflow_ширини_інтервалу_явно_відхиляється() =>
        Assert.Throws<ArithmeticException>(() => SeriesStatistics.ConfidenceInterval([0, 0.5], "м", double.Epsilon));

    [Fact]
    public void Незбіжність_екстремального_квантиля_має_пояснену_помилку()
    {
        var exception = Assert.Throws<ArithmeticException>(() =>
            SeriesStatistics.ConfidenceInterval([0, 1, 2], "м", 1 - 1e-12));
        Assert.Contains("Квантиль", exception.Message);
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public void Додатна_ширина_інтервалу_не_округлюється_мовчки_до_нуля() =>
        Assert.Throws<ArithmeticException>(() => SeriesStatistics.ConfidenceInterval([0, 2], "м", 1e-20));

    [Fact]
    public void Малий_рівень_довіри_для_df1_збігається_з_Коші()
    {
        var result = SeriesStatistics.ConfidenceInterval([0, 2], "м", 0.25);
        Assert.Equal(1 + Math.Tan(Math.PI / 8), result.Upper, 12);
    }

    [Fact]
    public void Дуже_великий_розмах_явно_відхиляється() =>
        Assert.Throws<OverflowException>(() => SeriesStatistics.Calculate([0, 1e160], "м"));

    [Fact]
    public void Відємна_серія_й_тримінг_одиниці_коректні()
    {
        var result = SeriesStatistics.Calculate([-3, -2, -1], " м ");
        Assert.Equal(-2, result.Mean);
        Assert.Equal(1, result.SampleStandardDeviation);
        Assert.Equal("м", result.Unit);
    }

    [Fact]
    public void Вибіркова_статистика_збігається_з_ручним_еталоном()
    {
        // Сума 40, сума квадратів відхилень 32, вибіркова дисперсія 32/7.
        var result = SeriesStatistics.Calculate([2, 4, 4, 4, 5, 5, 7, 9], "°C");

        Assert.Equal(8, result.Count);
        Assert.Equal("°C", result.Unit);
        Assert.Equal(5, result.Mean);
        Assert.Equal(Math.Sqrt(32d / 7), result.SampleStandardDeviation, 12);
        Assert.Equal(Math.Sqrt(4d / 7), result.StandardError, 12);
    }
}
