namespace Lab.Contracts.Instruments;

public sealed record InstrumentDto(int Id, int OwnerId, string Name, string SerialNumber,
    string Unit, double MinValue, double MaxValue, double AbsoluteError);
