using System.ComponentModel.DataAnnotations;
using Lab.Contracts.Validation;

namespace Lab.Contracts.Experiments;

public sealed record StatisticsQuery
{
    [FiniteNumber, Range(0d, 1d, MinimumIsExclusive = true, MaximumIsExclusive = true)]
    public double ConfidenceLevel { get; init; } = 0.95;
}
