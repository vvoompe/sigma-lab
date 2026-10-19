using System.ComponentModel.DataAnnotations;

namespace Lab.Contracts.Experiments;

public sealed record RenameExperimentRequest
{
    [Required, MaxLength(100)] public string Name { get; init; } = string.Empty;
    [Required, Range(typeof(long), "1", "9223372036854775807")] public long? ExpectedVersion { get; init; }
}
