using System.ComponentModel.DataAnnotations;

namespace Lab.Contracts.Validation;

/// <summary>Вхідна дата має явно містити UTC (суфікс Z у JSON).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class UtcTimestampAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is null || value is DateTime date && date.Kind == DateTimeKind.Utc;
}
