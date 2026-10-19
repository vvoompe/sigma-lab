namespace Lab.Contracts.Experiments;

public sealed record ExperimentDto(int Id, string Name, int SiteId, int InstrumentId,
    int? ReferenceValueId, string Status, DateTime CreatedAt, DateTime? SignedAt,
    int? SignedById, long Version, string Unit);
