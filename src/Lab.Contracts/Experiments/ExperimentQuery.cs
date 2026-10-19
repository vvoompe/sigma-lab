using System.ComponentModel.DataAnnotations;

namespace Lab.Contracts.Experiments;

public sealed class ExperimentQuery : ResourcePageQuery
{
    [Range(1, int.MaxValue)] public int? SiteId { get; init; }
    [RegularExpression("^(Draft|SignedOff)$")] public string? Status { get; init; }
}
