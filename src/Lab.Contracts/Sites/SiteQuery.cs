using System.ComponentModel.DataAnnotations;

namespace Lab.Contracts.Sites;

/// <summary>Фільтри та пагінація списку майданчиків. Межі координат включаються.</summary>
public sealed class SiteQuery : IValidatableObject
{
    /// <summary>Найбільша дозволена кількість елементів на сторінці.</summary>
    public const int MaxPageSize = 100;

    /// <summary>Номер сторінки, починаючи з одиниці.</summary>
    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    /// <summary>Розмір сторінки: типово 20; значення понад 100 обмежуються до 100.</summary>
    [Range(1, int.MaxValue)]
    public int PageSize { get; init; } = 20;

    /// <summary>Буквальний фрагмент назви з урахуванням регістру; пробіли по краях відкидаються.</summary>
    [StringLength(100)]
    public string? Name { get; init; }

    /// <summary>Нижня межа широти.</summary>
    [Range(-90d, 90d)]
    public double? MinLatitude { get; init; }

    /// <summary>Верхня межа широти.</summary>
    [Range(-90d, 90d)]
    public double? MaxLatitude { get; init; }

    /// <summary>Нижня межа довготи.</summary>
    [Range(-180d, 180d)]
    public double? MinLongitude { get; init; }

    /// <summary>Верхня межа довготи.</summary>
    [Range(-180d, 180d)]
    public double? MaxLongitude { get; init; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinLatitude > MaxLatitude)
        {
            yield return new ValidationResult(
                "Мінімальна широта не може перевищувати максимальну.",
                [nameof(MinLatitude), nameof(MaxLatitude)]);
        }

        if (MinLongitude > MaxLongitude)
        {
            yield return new ValidationResult(
                "Мінімальна довгота не може перевищувати максимальну.",
                [nameof(MinLongitude), nameof(MaxLongitude)]);
        }
    }
}
