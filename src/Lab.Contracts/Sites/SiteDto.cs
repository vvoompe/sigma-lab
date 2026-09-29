namespace Lab.Contracts.Sites;

/// <summary>Майданчик у відповіді API.</summary>
/// <param name="Id">Ідентифікатор.</param>
/// <param name="Name">Назва.</param>
/// <param name="Latitude">Широта.</param>
/// <param name="Longitude">Довгота.</param>
/// <param name="CreatedAt">Момент створення в UTC.</param>
public sealed record SiteDto(int Id, string Name, double Latitude, double Longitude, DateTime CreatedAt);
