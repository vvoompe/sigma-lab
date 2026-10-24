namespace Lab.Contracts.ReferenceValues;

/// <summary>Еталон у відповіді API.</summary>
/// <param name="Id">Ідентифікатор.</param>
/// <param name="Name">Назва.</param>
/// <param name="Unit">Одиниця виміру.</param>
/// <param name="Value">Значення еталона.</param>
/// <param name="AbsoluteTolerance">Абсолютний допуск у тій самій одиниці.</param>
public sealed record ReferenceValueDto(
    int Id, string Name, string Unit, double Value, double AbsoluteTolerance);
