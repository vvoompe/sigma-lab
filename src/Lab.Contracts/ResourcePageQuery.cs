using System.ComponentModel.DataAnnotations;

namespace Lab.Contracts;

/// <summary>Обмежена сторінка; завеликий PageSize нормалізує сервіс.</summary>
public class ResourcePageQuery
{
    public const int MaxPageSize = 100;
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, int.MaxValue)] public int PageSize { get; init; } = 20;
    [MaxLength(100)] public string? Name { get; init; }
}
