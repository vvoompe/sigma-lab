using System.ComponentModel.DataAnnotations;

namespace Lab.Contracts.Experiments;

public sealed record CreateExperimentRequest
{
    [Required, MaxLength(100)] public string Name { get; init; } = string.Empty;
    [Required, Range(1, int.MaxValue)] public int? SiteId { get; init; }
    [Required, Range(1, int.MaxValue)] public int? InstrumentId { get; init; }
    [Range(1, int.MaxValue)] public int? ReferenceValueId { get; init; }
}
