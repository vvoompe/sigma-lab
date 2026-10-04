using Lab.Application.Abstractions;
using Lab.Contracts.Sites;
using Lab.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Lab.Application.Sites;

/// <summary>
/// Приклад реалізації сервісу: тільки LINQ через <see cref="IAppDbContext"/>,
/// жодного SQL і жодної згадки про конкретну СУБД.
/// </summary>
/// <param name="db">Контекст бази даних.</param>
public sealed class SiteService(IAppDbContext db) : ISiteService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SiteDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var sites = await db.Sites
            .AsNoTracking()
            .OrderBy(site => site.Name)
            .Select(site => new SiteDto(site.Id, site.Name, site.Latitude, site.Longitude, site.CreatedAt))
            .ToListAsync(cancellationToken);

        return sites;
    }

    /// <inheritdoc />
    public async Task<SiteDto?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var site = await db.Sites
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new SiteDto(item.Id, item.Name, item.Latitude, item.Longitude, item.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        return site;
    }

    /// <inheritdoc />
    public async Task<SiteDto> CreateAsync(CreateSiteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Правила домену перевіряємо ще до звернення до бази: у БД не має потрапляти
        // те, що не є коректним за предметною областю.
        if (!Site.IsNameValid(request.Name))
        {
            throw new ArgumentException(
                $"Назва майданчика має містити від {Site.MinNameLength} до {Site.MaxNameLength} символів.",
                nameof(request));
        }

        if (!Site.IsLatitudeInRange(request.Latitude))
        {
            throw new ArgumentException("Широта поза припустимим діапазоном.", nameof(request));
        }

        if (!Site.IsLongitudeInRange(request.Longitude))
        {
            throw new ArgumentException("Довгота поза припустимим діапазоном.", nameof(request));
        }

        var site = new Site
        {
            Name = request.Name.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            CreatedAt = DateTime.UtcNow,
        };

        db.Sites.Add(site);
        await db.SaveChangesAsync(cancellationToken);

        return new SiteDto(site.Id, site.Name, site.Latitude, site.Longitude, site.CreatedAt);
    }
}
