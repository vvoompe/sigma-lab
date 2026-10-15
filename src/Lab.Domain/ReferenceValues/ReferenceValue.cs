using Lab.Domain.Validation;

namespace Lab.Domain.ReferenceValues;

/// <summary>Іменований еталон у власній одиниці з абсолютним допуском.</summary>
public sealed class ReferenceValue
{
    private ReferenceValue() { }

    public ReferenceValue(string name, string unit, double value, double absoluteTolerance)
    {
        Name = DomainRules.Text(name, nameof(name));
        Unit = DomainRules.Text(unit, nameof(unit), 32);
        DomainRules.Finite(value, nameof(value));
        DomainRules.Finite(absoluteTolerance, nameof(absoluteTolerance));
        ArgumentOutOfRangeException.ThrowIfNegative(absoluteTolerance);
        Value = value;
        AbsoluteTolerance = absoluteTolerance;
    }

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Unit { get; private set; } = string.Empty;
    public double Value { get; private set; }
    public double AbsoluteTolerance { get; private set; }
}
