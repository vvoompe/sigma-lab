using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Lab.Application.Abstractions;
using Lab.Application.Security;
using Lab.Contracts;
using Lab.Contracts.Sites;
using Lab.Domain.Audit;
using Lab.Domain.Security;
using Lab.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Lab.Application.Sites;

/// <summary>
/// Приклад реалізації сервісу: тільки LINQ через <see cref="IAppDbContext"/>,
/// жодного SQL і жодної згадки про конкретну СУБД.
/// </summary>
/// <param name="db">Контекст бази даних.</param>
/// <param name="currentUser">Поточний користувач із перевірених claims.</param>
public sealed class SiteService(IAppDbContext db, ICurrentUser currentUser) : ISiteService
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

        var ownerId = RequireCurrentUserId();
        var filtered = db.Sites.AsNoTracking().Where(site => site.OwnerId == ownerId);
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
            .Select(site => new SiteDto(site.Id, site.Name, site.Latitude, site.Longitude, site.CreatedAt, site.ManagerId))
            .ToListAsync(cancellationToken);

        return new PagedResult<SiteDto>(sites, totalCount, query.Page, pageSize);
    }

    /// <inheritdoc />
    public async Task<SiteDto?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var ownerId = RequireCurrentUserId();
        var site = await db.Sites
            .AsNoTracking()
            .Where(item => item.Id == id && item.OwnerId == ownerId)
            .Select(item => new SiteDto(item.Id, item.Name, item.Latitude, item.Longitude, item.CreatedAt, item.ManagerId))
            .FirstOrDefaultAsync(cancellationToken);

        return site;
    }

    /// <inheritdoc />
    public async Task<SiteDto> CreateAsync(CreateSiteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateSite(request.Name, request.Latitude, request.Longitude);

        var site = new Site
        {
            Name = request.Name.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            OwnerId = RequireCurrentUserId(),
            CreatedAt = DateTime.UtcNow,
        };

        db.Sites.Add(site);
        await db.SaveChangesAsync(cancellationToken);

        return new SiteDto(site.Id, site.Name, site.Latitude, site.Longitude, site.CreatedAt, site.ManagerId);
    }

    /// <inheritdoc />
    public async Task<SiteUpdateStatus> UpdateAsync(
        int id,
        UpdateSiteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var site = await db.Sites.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (site is null)
        {
            return SiteUpdateStatus.NotFound;
        }

        if (site.OwnerId != RequireCurrentUserId())
        {
            return SiteUpdateStatus.Forbidden;
        }

        if (request.Latitude is not { } latitude || request.Longitude is not { } longitude)
        {
            throw new ArgumentException("Для повного оновлення потрібні обидві координати.", nameof(request));
        }

        ValidateSite(request.Name, latitude, longitude);
        site.Name = request.Name.Trim();
        site.Latitude = latitude;
        site.Longitude = longitude;
        await db.SaveChangesAsync(cancellationToken);

        return SiteUpdateStatus.Updated;
    }

    /// <inheritdoc />
    public async Task<SiteManagerAssignmentStatus> AssignManagerAsync(
        int id,
        AssignSiteManagerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actorId = RequireCurrentUserId();
        if (!string.Equals(currentUser.Role, nameof(UserRole.Administrator), StringComparison.Ordinal))
        {
            return SiteManagerAssignmentStatus.Forbidden;
        }

        if (request.ManagerId is not { } managerId)
        {
            throw new ArgumentException("Для призначення потрібен ідентифікатор керівника.", nameof(request));
        }

        var site = await db.Sites.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (site is null)
        {
            return SiteManagerAssignmentStatus.SiteNotFound;
        }

        // Роль живе в облікових даних, тому домен її не перевіряє - робимо це тут.
        var credential = await db.UserCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.UserId == managerId, cancellationToken);
        if (credential is null)
        {
            return SiteManagerAssignmentStatus.ManagerNotFound;
        }

        if (credential.Role != UserRole.LabManager)
        {
            return SiteManagerAssignmentStatus.ManagerRoleMissing;
        }

        var previousManagerId = site.ManagerId;
        if (!site.AssignManager(managerId))
        {
            // Той самий керівник уже закріплений: не пишемо ні змін, ні аудиту.
            return SiteManagerAssignmentStatus.Assigned;
        }

        var details = string.Format(
            CultureInfo.InvariantCulture,
            "managerId={0}; previous={1}",
            managerId,
            previousManagerId?.ToString(CultureInfo.InvariantCulture) ?? "none");
        db.AuditEntries.Add(new AuditEntry(
            actorId,
            previousManagerId is null ? "site.manager_assigned" : "site.manager_changed",
            "Site",
            site.Id,
            DateTime.UtcNow,
            details));
        await db.SaveChangesAsync(cancellationToken);

        return SiteManagerAssignmentStatus.Assigned;
    }

    private int RequireCurrentUserId() =>
        currentUser.Id is > 0 and var ownerId
            ? ownerId
            : throw new InvalidOperationException("Перевірений поточний користувач має містити додатний Id.");

    private static void ValidateSite(string name, double latitude, double longitude)
    {
        if (!Site.IsNameValid(name))
        {
            throw new ArgumentException(
                $"Назва майданчика має містити від {Site.MinNameLength} до {Site.MaxNameLength} символів.",
                nameof(name));
        }

        if (!Site.IsLatitudeInRange(latitude))
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), "Широта поза припустимим діапазоном.");
        }

        if (!Site.IsLongitudeInRange(longitude))
        {
            throw new ArgumentOutOfRangeException(nameof(longitude), "Довгота поза припустимим діапазоном.");
        }
    }
}
