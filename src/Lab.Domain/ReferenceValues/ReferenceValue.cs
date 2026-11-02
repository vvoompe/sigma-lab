using Lab.Domain.Validation;

namespace Lab.Domain.ReferenceValues;

/// <summary>Іменований еталон у власній одиниці з абсолютним допуском.</summary>
public sealed class ReferenceValue
{
    private ReferenceValue() { }

    public ReferenceValue(string name, string unit, double value, double absoluteTolerance)
    {
        Unit = DomainRules.Text(unit, nameof(unit), 32);
        Update(name, unit, value, absoluteTolerance);
    }

    /// <summary>
    /// Змінює назву, значення й допуск. Одиниця незмінна після створення:
    /// це зберігає сумісність і з серією, яка створюється паралельно.
    /// </summary>
    public void Update(string name, string unit, double value, double absoluteTolerance)
    {
        var validatedName = DomainRules.Text(name, nameof(name));
        var validatedUnit = DomainRules.Text(unit, nameof(unit), 32);
        DomainRules.Finite(value, nameof(value));
        DomainRules.Finite(absoluteTolerance, nameof(absoluteTolerance));
        ArgumentOutOfRangeException.ThrowIfNegative(absoluteTolerance);
        if (!string.Equals(Unit, validatedUnit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Одиницю еталона змінювати не можна; створіть новий еталон.");
        }

        Name = validatedName;
        Value = value;
        AbsoluteTolerance = absoluteTolerance;
    }

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Unit { get; private set; } = string.Empty;
    public double Value { get; private set; }
    public double AbsoluteTolerance { get; private set; }
}
