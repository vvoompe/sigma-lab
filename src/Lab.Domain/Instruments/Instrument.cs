using Lab.Domain.Validation;

namespace Lab.Domain.Instruments;

/// <summary>Прилад із незмінними метрологічними параметрами для збереження сенсу старих вимірів.</summary>
public sealed class Instrument
{
    private Instrument() { }

    public Instrument(int ownerId, string name, string serialNumber, string unit, double minValue, double maxValue, double absoluteError)
    {
        DomainRules.PositiveId(ownerId, nameof(ownerId));
        Name = DomainRules.Text(name, nameof(name));
        SerialNumber = DomainRules.Text(serialNumber, nameof(serialNumber));
        Unit = DomainRules.Text(unit, nameof(unit), 32);
        DomainRules.Finite(minValue, nameof(minValue));
        DomainRules.Finite(maxValue, nameof(maxValue));
        DomainRules.Finite(absoluteError, nameof(absoluteError));
        if (minValue >= maxValue || absoluteError < 0)
        {
            throw new ArgumentException("Потрібно min < max та невід'ємна абсолютна похибка.");
        }

        OwnerId = ownerId;
        MinValue = minValue;
        MaxValue = maxValue;
        AbsoluteError = absoluteError;
    }

    public int Id { get; private set; }
    public int OwnerId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string SerialNumber { get; private set; } = string.Empty;
    public string Unit { get; private set; } = string.Empty;
    public double MinValue { get; private set; }
    public double MaxValue { get; private set; }
    public double AbsoluteError { get; private set; }

    public void ValidateMeasurement(double value, string unit)
    {
        DomainRules.Finite(value, nameof(value));
        if (value < MinValue || value > MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"Вимір має бути в межах [{MinValue}, {MaxValue}].");
        }

        if (!string.Equals(Unit, unit?.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Одиниця виміру має збігатися з одиницею приладу.", nameof(unit));
        }
    }
}
