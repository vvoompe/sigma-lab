namespace Lab.Application.Statistics;

/// <summary>Позначений вимір; індекс зберігає його місце у вихідній серії.</summary>
public sealed record Outlier(int Index, double Value);

/// <summary>Абсолютне відхилення від еталона й включний допуск у спільній одиниці.</summary>
public sealed record ReferenceComparison(
    double AbsoluteDeviation, double AbsoluteTolerance, bool WithinTolerance, string Unit);

/// <summary>Аналіз однорідних вимірів без залежності від БД.</summary>
public static class MeasurementAnalysis
{
    /// <summary>
    /// Позначає |x−mean| / s > sigmaThreshold. s - вибіркове СКО; дані не видаляються.
    /// Правило є евристикою, а не доказом помилки виміру; для малих n має низьку чутливість.
    /// </summary>
    public static IReadOnlyList<Outlier> FindOutliers(
        IEnumerable<double> values, string unit, double sigmaThreshold = 3)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (!double.IsFinite(sigmaThreshold) || sigmaThreshold <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sigmaThreshold), "Поріг має бути додатним скінченним числом.");
        }

        var data = values.ToArray();
        var statistics = SeriesStatistics.Calculate(data, unit);
        if (statistics.SampleStandardDeviation == 0)
        {
            return [];
        }

        return data.Select((value, index) => new Outlier(index, value))
            .Where(item => Math.Abs(item.Value - statistics.Origin - statistics.MeanOffset)
                / statistics.SampleStandardDeviation > sigmaThreshold)
            .ToArray();
    }

    /// <summary>Порівнює значення з еталоном без автоматичного перетворення одиниць.</summary>
    public static ReferenceComparison CompareToReference(
        double value, string unit, double reference, string referenceUnit, double absoluteTolerance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unit);
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceUnit);
        if (!string.Equals(unit.Trim(), referenceUnit.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Одиниці вимірювання та еталона мають збігатися.", nameof(referenceUnit));
        }

        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        if (!double.IsFinite(reference))
        {
            throw new ArgumentOutOfRangeException(nameof(reference));
        }

        if (!double.IsFinite(absoluteTolerance) || absoluteTolerance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(absoluteTolerance));
        }

        var deviation = Math.Abs(value - reference);
        if (!double.IsFinite(deviation))
        {
            throw new OverflowException("Відхилення не вміщується в double.");
        }

        return new ReferenceComparison(deviation, absoluteTolerance, deviation <= absoluteTolerance, unit.Trim());
    }
}
