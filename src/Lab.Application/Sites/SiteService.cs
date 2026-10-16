using System.ComponentModel.DataAnnotations;
using Lab.Application.Abstractions;
using Lab.Contracts;
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
    public async Task<PagedResult<SiteDto>> ListAsync(SiteQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(query, new ValidationContext(query), validationResults, validateAllProperties: true))
        {
            throw new ArgumentException(string.Join(" ", validationResults.Select(result => result.ErrorMessage)), nameof(query));
        }

        var filtered = db.Sites.AsNoTracking();
        var name = query.Name?.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            filtered = filtered.Where(site => site.Name.Contains(name));
        }

        if (query.MinLatitude is { } minLatitude)
        {
            filtered = filtered.Where(site => site.Latitude >= minLatitude);
        }

        if (query.MaxLatitude is { } maxLatitude)
        {
            filtered = filtered.Where(site => site.Latitude <= maxLatitude);
        }

        if (query.MinLongitude is { } minLongitude)
        {
            filtered = filtered.Where(site => site.Longitude >= minLongitude);
        }

        if (query.MaxLongitude is { } maxLongitude)
        {
            filtered = filtered.Where(site => site.Longitude <= maxLongitude);
        }

        var totalCount = await filtered.CountAsync(cancellationToken);
        var pageSize = Math.Min(query.PageSize, SiteQuery.MaxPageSize);
        // long запобігає переповненню для page=int.MaxValue; далека сторінка є порожньою.
        var offset = (query.Page - 1L) * pageSize;
        if (offset >= totalCount)
        {
            return new PagedResult<SiteDto>([], totalCount, query.Page, pageSize);
        }

        var sites = await filtered
            .OrderBy(site => site.Name)
            .ThenBy(site => site.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .Select(site => new SiteDto(site.Id, site.Name, site.Latitude, site.Longitude, site.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<SiteDto>(sites, totalCount, query.Page, pageSize);
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
