using Lab.Domain.Instruments;
using Lab.Domain.Measurements;
using Lab.Domain.ReferenceValues;
using Lab.Domain.Validation;

namespace Lab.Domain.Experiments;

/// <summary>Серія одного приладу. Авторизацію підпису перевіряє сервіс Z3.</summary>
public sealed class Experiment
{
    private readonly List<Measurement> measurements = [];
    private Experiment() { }

    public Experiment(int siteId, int ownerId, string name, Instrument instrument, ReferenceValue? referenceValue = null)
    {
        DomainRules.PositiveId(siteId, nameof(siteId));
        DomainRules.PositiveId(ownerId, nameof(ownerId));
        ArgumentNullException.ThrowIfNull(instrument);
        if (instrument.OwnerId != ownerId)
        {
            throw new ArgumentException("Прилад має належати власнику серії.", nameof(instrument));
        }

        if (referenceValue is not null && referenceValue.Unit != instrument.Unit)
        {
            throw new ArgumentException("Еталон і прилад мають бути в одній одиниці.", nameof(referenceValue));
        }

        SiteId = siteId;
        OwnerId = ownerId;
        Name = DomainRules.Text(name, nameof(name));
        Instrument = instrument;
        InstrumentId = instrument.Id;
        ReferenceValue = referenceValue;
        ReferenceValueId = referenceValue?.Id;
    }

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int SiteId { get; private set; }
    public int OwnerId { get; private set; }
    public int InstrumentId { get; private set; }
    public Instrument? Instrument { get; private set; }
    public int? ReferenceValueId { get; private set; }
    public ReferenceValue? ReferenceValue { get; private set; }
    public ExperimentStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? SignedAt { get; private set; }
    public int? SignedById { get; private set; }
    public long Version { get; private set; } = 1;
    public IReadOnlyCollection<Measurement> Measurements => measurements.AsReadOnly();
    public string Unit => GetInstrument().Unit;

    public Measurement AddMeasurement(Guid clientId, double value, string unit, DateTime measuredAt)
    {
        EnsureDraft();
        if (measurements.Any(item => item.ClientId == clientId))
        {
            throw new InvalidOperationException("ClientId вже є у серії.");
        }

        var measurement = new Measurement(this, clientId, value, unit, measuredAt);
        measurements.Add(measurement);
        Changed();
        return measurement;
    }

    public void RemoveMeasurement(Measurement measurement)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(measurement);
        if (!measurements.Remove(measurement))
        {
            throw new ArgumentException("Вимір не належить завантаженій серії.", nameof(measurement));
        }

        Changed();
    }

    /// <summary>Зміна назви серії. Дозволено лише в чернетці.</summary>
    public void Rename(string name)
    {
        EnsureDraft();
        var validated = DomainRules.Text(name, nameof(name));
        if (string.Equals(validated, Name, StringComparison.Ordinal))
        {
            return;
        }

        Name = validated;
        Changed();
    }

    public void Sign(int signedById, DateTime signedAt)
    {
        EnsureDraft();
        DomainRules.PositiveId(signedById, nameof(signedById));
        DomainRules.Utc(signedAt, nameof(signedAt));
        SignedById = signedById;
        SignedAt = signedAt;
        Status = ExperimentStatus.SignedOff;
        Changed();
    }

    internal Instrument GetInstrument() => Instrument ?? throw new InvalidOperationException("Спочатку завантажте прилад серії.");
    internal void EnsureDraft()
    {
        if (Status != ExperimentStatus.Draft)
        {
            throw new InvalidOperationException("Підписану серію не можна змінювати.");
        }
    }

    internal void Changed() => Version = checked(Version + 1);
}
