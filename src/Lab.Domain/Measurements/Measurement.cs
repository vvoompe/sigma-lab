using Lab.Domain.Experiments;
using Lab.Domain.Validation;

namespace Lab.Domain.Measurements;

/// <summary>Вимір створюється тільки через серію; одиниця визначається її приладом.</summary>
public sealed class Measurement
{
    private Measurement() { }

    internal Measurement(Experiment experiment, Guid clientId, double value, string unit, DateTime measuredAt)
    {
        if (clientId == Guid.Empty)
        {
            throw new ArgumentException("ClientId не може бути порожнім UUID.", nameof(clientId));
        }

        experiment.GetInstrument().ValidateMeasurement(value, unit);
        DomainRules.Utc(measuredAt, nameof(measuredAt));
        Experiment = experiment;
        ExperimentId = experiment.Id;
        ClientId = clientId;
        Value = value;
        MeasuredAt = measuredAt;
    }

    public int Id { get; private set; }
    public int ExperimentId { get; private set; }
    public Experiment? Experiment { get; private set; }
    public Guid ClientId { get; private set; }
    public double Value { get; private set; }
    public DateTime MeasuredAt { get; private set; }
    public long Version { get; private set; } = 1;

    public void Update(double value, string unit, DateTime measuredAt)
    {
        var series = Experiment ?? throw new InvalidOperationException("Спочатку завантажте серію виміру.");
        series.EnsureDraft();
        series.GetInstrument().ValidateMeasurement(value, unit);
        DomainRules.Utc(measuredAt, nameof(measuredAt));
        Value = value;
        MeasuredAt = measuredAt;
        Version = checked(Version + 1);
        series.Changed();
    }
}
