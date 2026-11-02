using System.ComponentModel.DataAnnotations;

namespace Lab.Contracts.Experiments;

public sealed record SignExperimentRequest
{
    [Required, Range(typeof(long), "1", "9223372036854775807")]
    public long? ExpectedVersion { get; init; }
}
