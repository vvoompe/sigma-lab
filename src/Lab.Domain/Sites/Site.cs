namespace Lab.Domain.Sites;

/// <summary>
/// Майданчик (точка відбору), на якому проводяться серії вимірювань.
/// Клас - звичайний POCO: жодних EF-атрибутів, конфігурація живе в шарі інфраструктури.
/// </summary>
public sealed class Site
{
    /// <summary>Мінімально припустима широта.</summary>
    public const double MinLatitude = -90d;

    /// <summary>Максимально припустима широта.</summary>
    public const double MaxLatitude = 90d;

    /// <summary>Мінімально припустима довгота.</summary>
    public const double MinLongitude = -180d;

    /// <summary>Максимально припустима довгота.</summary>
    public const double MaxLongitude = 180d;

    /// <summary>Мінімальна довжина назви майданчика.</summary>
    public const int MinNameLength = 3;

    /// <summary>Максимальна довжина назви майданчика.</summary>
    public const int MaxNameLength = 100;

    /// <summary>Ідентифікатор.</summary>
    public int Id { get; set; }

    /// <summary>Назва майданчика.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Широта в градусах.</summary>
    public double Latitude { get; set; }

    /// <summary>Довгота в градусах.</summary>
    public double Longitude { get; set; }

    /// <summary>Ідентифікатор власника (дослідника, який створив майданчик).</summary>
    public int? OwnerId { get; set; }

    /// <summary>Старий ідентифікатор до появи User; не є підтвердженим власником.</summary>
    public int LegacyOwnerId { get; private set; }

    /// <summary>
    /// Ідентифікатор керівника лабораторії, закріпленого за майданчиком.
    /// Відрізняється від <see cref="OwnerId"/>: власник - автор майданчика, менеджер - той,
    /// кому доручено вести серії на цьому майданчику.
    /// </summary>
    public int? ManagerId { get; private set; }

    /// <summary>
    /// Закріплює керівника лабораторії за майданчиком.
    /// Повертає <c>true</c>, якщо призначення змінилось, і <c>false</c>, якщо той самий
    /// користувач уже закріплений - щоб не писати зайвий запис аудиту.
    /// </summary>
    /// <remarks>
    /// Роль запрошеного перевіряє прикладний шар: домен не знає про облікові дані.
    /// </remarks>
    public bool AssignManager(int managerId)
    {
        Validation.DomainRules.PositiveId(managerId, nameof(managerId));
        if (ManagerId == managerId)
        {
            return false;
        }

        ManagerId = managerId;
        return true;
    }

    /// <summary>Момент створення запису в UTC.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Перевіряє, чи припустима назва майданчика.</summary>
    public static bool IsNameValid(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var trimmed = name.Trim();
        return trimmed.Length is >= MinNameLength and <= MaxNameLength;
    }

    /// <summary>Перевіряє, чи належить широта припустимому діапазону.</summary>
    public static bool IsLatitudeInRange(double latitude) =>
        latitude is >= MinLatitude and <= MaxLatitude && !double.IsNaN(latitude);

    /// <summary>Перевіряє, чи належить довгота припустимому діапазону.</summary>
    public static bool IsLongitudeInRange(double longitude) =>
        longitude is >= MinLongitude and <= MaxLongitude && !double.IsNaN(longitude);
}
