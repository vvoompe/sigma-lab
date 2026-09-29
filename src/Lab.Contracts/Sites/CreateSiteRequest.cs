using System.ComponentModel.DataAnnotations;
using Lab.Domain.Sites;

namespace Lab.Contracts.Sites;

/// <summary>Запит на створення майданчика.</summary>
public sealed class CreateSiteRequest
{
    /// <summary>Назва майданчика.</summary>
    [Required(ErrorMessage = "Назва обов'язкова.")]
    [StringLength(Site.MaxNameLength, MinimumLength = Site.MinNameLength,
        ErrorMessage = "Назва має містити від 3 до 100 символів.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Широта в градусах.</summary>
    [Range(Site.MinLatitude, Site.MaxLatitude, ErrorMessage = "Широта має бути в межах від -90 до 90.")]
    public double Latitude { get; set; }

    /// <summary>Довгота в градусах.</summary>
    [Range(Site.MinLongitude, Site.MaxLongitude, ErrorMessage = "Довгота має бути в межах від -180 до 180.")]
    public double Longitude { get; set; }
}
