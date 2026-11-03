using Lab.Contracts.Instruments;
using Lab.Contracts.ReferenceValues;

namespace Lab.Contracts.Experiments;

/// <summary>Один дозволений для читання контекст серії, без розширення доступу до довідників.</summary>
public sealed record ExperimentContextDto(
    ExperimentDto Experiment, int OwnerId, ExperimentSiteDto Site,
    InstrumentDto Instrument, ReferenceValueDto? ReferenceValue);

/// <summary>Власність майданчика потрібна для відновлення спільної моделі в офлайн-кеші.</summary>
public sealed record ExperimentSiteDto(
    int Id, string Name, double Latitude, double Longitude, DateTime CreatedAt,
    int? OwnerId, int? ManagerId);
