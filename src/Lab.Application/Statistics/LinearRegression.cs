namespace Lab.Application.Statistics;

/// <summary>Лінійна МНК-апроксимація y=a*x+b; прогноз обчислюється в центрованій формі.</summary>
public sealed record LinearRegressionResult(
    double Slope, double Intercept, double XOrigin, double YOrigin,
    double XMeanOffset, double YMeanOffset,
    double RSquared, double RootMeanSquaredResidual, string XUnit, string YUnit)
{
    public double XMean => XOrigin + XMeanOffset;

    public double YMean => YOrigin + YMeanOffset;

    /// <summary>Обчислює y; екстраполяція не є гарантією фізичної достовірності.</summary>
    public double Predict(double x)
    {
        if (!double.IsFinite(x))
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        // Не складаємо XOrigin+XMeanOffset до віднімання: середнє може округлитись.
        var value = YOrigin + (YMeanOffset + Slope * (x - XOrigin - XMeanOffset));
        return double.IsFinite(value)
            ? value
            : throw new OverflowException("Прогноз не вміщується в double.");
    }
}

/// <summary>Чиста лінійна регресія з вільним перетином, без звернення до БД.</summary>
public static class LinearRegression
{
    /// <summary>
    /// Мінімізує суму квадратів залишків. Потрібно n≥2 та ненульовий розкид x.
    /// RMS=sqrt(SSE/n), не оцінка залишкового СКО sqrt(SSE/(n−2)). Для сталої y R² умовно 1.
    /// </summary>
    public static LinearRegressionResult Fit(
        IEnumerable<double> x, IEnumerable<double> y, string xUnit, string yUnit)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentException.ThrowIfNullOrWhiteSpace(xUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(yUnit);
        var xData = x.ToArray();
        var yData = y.ToArray();
        if (xData.Length != yData.Length || xData.Length < 2)
        {
            throw new ArgumentException("Потрібні однакові кількості x та y, щонайменше дві пари.", nameof(x));
        }

        var xOffsets = Center(xData, out var xMeanOffset);
        var yOffsets = Center(yData, out var yMeanOffset);
        double sxx = 0;
        double sxy = 0;
        double syy = 0;
        for (var index = 0; index < xOffsets.Length; index++)
        {
            sxx = Finite(sxx + xOffsets[index] * xOffsets[index]);
            sxy = Finite(sxy + xOffsets[index] * yOffsets[index]);
            syy = Finite(syy + yOffsets[index] * yOffsets[index]);
        }

        if (sxx == 0)
        {
            if (xData.Any(value => value != xData[0]))
            {
                throw new OverflowException("Ненульовий розкид x надто малий для double; змініть масштаб.");
            }

            throw new ArgumentException("Для сталої x регресія не визначена.", nameof(x));
        }

        if (syy == 0 && yData.Any(value => value != yData[0]))
        {
            throw new OverflowException("Ненульовий розкид y надто малий для double; змініть масштаб.");
        }

        var slope = Finite(sxy / sxx);
        var intercept = Finite((yData[0] - slope * xData[0]) + (yMeanOffset - slope * xMeanOffset));
        double sse = 0;
        for (var index = 0; index < xOffsets.Length; index++)
        {
            var residual = yOffsets[index] - slope * xOffsets[index];
            sse = Finite(sse + residual * residual);
        }

        return new LinearRegressionResult(
            slope, intercept, xData[0], yData[0], xMeanOffset, yMeanOffset,
            syy == 0 ? 1 : Math.Clamp(1 - sse / syy, 0, 1),
            Math.Sqrt(sse / xData.Length), xUnit.Trim(), yUnit.Trim());
    }

    private static double[] Center(double[] values, out double meanOffset)
    {
        var origin = values[0];
        meanOffset = 0;
        var offsets = new double[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            if (!double.IsFinite(values[index]))
            {
                throw new ArgumentException("Координати мають бути скінченними числами.", nameof(values));
            }

            offsets[index] = Finite(values[index] - origin);
            meanOffset = Finite(meanOffset + (offsets[index] - meanOffset) / (index + 1));
        }

        for (var index = 0; index < offsets.Length; index++)
        {
            offsets[index] -= meanOffset;
        }

        return offsets;
    }

    private static double Finite(double value) => double.IsFinite(value)
        ? value
        : throw new OverflowException("Проміжний результат регресії не представляється скінченним double; змініть масштаб.");
}
