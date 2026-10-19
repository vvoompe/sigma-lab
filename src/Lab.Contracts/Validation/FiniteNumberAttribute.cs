using System.ComponentModel.DataAnnotations;

namespace Lab.Contracts.Validation;

/// <summary>JSON/прямі виклики приймають лише скінченні числа.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class FiniteNumberAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is null || value is double number && double.IsFinite(number);
}
