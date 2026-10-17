using MathNet.Numerics.Distributions;

namespace Lab.Application.Statistics;

/// <summary>Двобічний довірчий інтервал середнього в одиницях вхідної серії.</summary>
public sealed record MeanConfidenceInterval(double Lower, double Upper, double ConfidenceLevel, string Unit);

/// <summary>Вибіркові оцінки однорідної серії в одній одиниці вимірювання.</summary>
public sealed record SeriesStatisticsResult(
    int Count,
    string Unit,
    double Mean,
    double SampleStandardDeviation,
    double StandardError)
{
    internal double Origin { get; init; }

    internal double MeanOffset { get; init; }
}

/// <summary>Чисті обчислення; вхідні значення не змінюються й не зберігаються.</summary>
public static class SeriesStatistics
{
    /// <summary>Обчислює середнє, вибіркове СКО (n−1) та стандартну похибку.</summary>
    public static SeriesStatisticsResult Calculate(IEnumerable<double> values, string unit)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentException.ThrowIfNullOrWhiteSpace(unit);
        var data = values.ToArray();
        if (data.Length < 2)
        {
            throw new ArgumentException("Для вибіркового СКО потрібні щонайменше два виміри.", nameof(values));
        }

        // Віднімання опорного значення захищає малі відхилення на великому фоні.
        var origin = data[0];
        double meanOffset = 0;
        for (var index = 0; index < data.Length; index++)
        {
            if (!double.IsFinite(data[index]))
            {
                throw new ArgumentException("Виміри мають бути скінченними числами.", nameof(values));
            }

            var offset = RequireFinite(data[index] - origin);
            meanOffset = RequireFinite(meanOffset + (offset - meanOffset) / (index + 1));
        }

        double sum = 0;
        double compensation = 0;
        foreach (var value in data)
        {
            var deviation = value - origin - meanOffset;
            var term = RequireFinite(deviation * deviation) - compensation;
            var next = RequireFinite(sum + term);
            compensation = next - sum - term;
            sum = next;
        }

        var variance = sum / (data.Length - 1);
        if (variance == 0 && data.Any(value => value != origin))
        {
            throw new OverflowException("Ненульова дисперсія надто мала для double; змініть масштаб одиниць.");
        }

        var standardDeviation = Math.Sqrt(variance);
        return new SeriesStatisticsResult(
            data.Length, unit.Trim(), RequireFinite(origin + meanOffset),
            standardDeviation, standardDeviation / Math.Sqrt(data.Length))
        {
            Origin = origin,
            MeanOffset = meanOffset,
        };
    }

    /// <summary>
    /// Двобічний t-інтервал середнього: незалежні нормально розподілені виміри,
    /// невідома дисперсія. Рівень довіри строго між 0 та 1.
    /// </summary>
    public static MeanConfidenceInterval ConfidenceInterval(
        IEnumerable<double> values, string unit, double confidenceLevel = 0.95)
    {
        if (!double.IsFinite(confidenceLevel) || confidenceLevel <= 0 || confidenceLevel >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(confidenceLevel), "Рівень довіри має належати (0, 1).");
        }

        var statistics = Calculate(values, unit);
        if (statistics.StandardError == 0)
        {
            return new MeanConfidenceInterval(statistics.Mean, statistics.Mean, confidenceLevel, statistics.Unit);
        }

        // Для df=1 розподіл є Коші; аналітичний квантиль стійкий навіть біля confidence=1.
        double critical;
        if (statistics.Count == 2)
        {
            critical = confidenceLevel <= 0.5
                ? Math.Tan(Math.PI * confidenceLevel / 2)
                : 1 / Math.Tan(Math.PI * (1 - confidenceLevel) / 2);
        }
        else
        {
            try
            {
                critical = -StudentT.InvCDF(0, 1, statistics.Count - 1, (1 - confidenceLevel) / 2);
            }
            catch (MathNet.Numerics.NonConvergenceException exception)
            {
                throw new ArithmeticException("Квантиль не збігся для цього рівня довіри; оберіть менш екстремальний рівень.", exception);
            }
        }

        var margin = RequireFinite(critical * statistics.StandardError);
        if (critical <= 0 || margin == 0)
        {
            throw new ArithmeticException("Додатна ширина інтервалу не представляється в double.");
        }
        var lower = RequireFinite(statistics.Mean - margin);
        var upper = RequireFinite(statistics.Mean + margin);
        if (lower == upper)
        {
            throw new ArithmeticException("Межі ненульового інтервалу збігаються після округлення double.");
        }

        return new MeanConfidenceInterval(lower, upper, confidenceLevel, statistics.Unit);
    }

    private static double RequireFinite(double value) => double.IsFinite(value)
        ? value
        : throw new OverflowException("Проміжний результат статистики не представляється скінченним double; змініть масштаб.");
}
