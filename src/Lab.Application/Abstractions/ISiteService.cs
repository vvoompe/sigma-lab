using Lab.Contracts.Sites;

namespace Lab.Application.Abstractions;

/// <summary>Операції над майданчиками (зона Z1 розширює цей сервіс своїми сценаріями).</summary>
public interface ISiteService
{
    /// <summary>Повертає всі майданчики.</summary>
    Task<IReadOnlyList<SiteDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Повертає майданчик за ідентифікатором або <c>null</c>.</summary>
    Task<SiteDto?> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Створює майданчик і повертає його контракт.</summary>
    Task<SiteDto> CreateAsync(CreateSiteRequest request, CancellationToken cancellationToken = default);
}
