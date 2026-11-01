using System.ComponentModel.DataAnnotations;
using Lab.Application.Abstractions;
using Lab.Application.Resources;
using Lab.Application.Security;
using Lab.Contracts;
using Lab.Contracts.ReferenceValues;
using Lab.Domain.ReferenceValues;
using Lab.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Lab.Application.ReferenceValues;

/// <summary>
/// Довідник еталонів. Схема не змінюється: таблиця створена міграцією DomainModel,
/// а серії вже посилаються на неї зовнішнім ключем.
/// </summary>
public sealed class ReferenceValueService(IAppDbContext db, ICurrentUser currentUser) : IReferenceValueService
{
    /// <inheritdoc />
    public async Task<PagedResult<ReferenceValueDto>> ListAsync(
        ResourcePageQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        _ = ResourceValidation.UserId(currentUser);
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(query, new ValidationContext(query), results, validateAllProperties: true))
        {
            throw new ArgumentException(string.Join(" ", results.Select(result => result.ErrorMessage)), nameof(query));
        }

        var filtered = db.ReferenceValues.AsNoTracking();
        var name = query.Name?.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            filtered = filtered.Where(item => item.Name.Contains(name));
        }

        var totalCount = await filtered.CountAsync(cancellationToken);
        var pageSize = Math.Min(query.PageSize, ResourcePageQuery.MaxPageSize);
        var offset = (query.Page - 1L) * pageSize;
        if (offset >= totalCount)
        {
            return new PagedResult<ReferenceValueDto>([], totalCount, query.Page, pageSize);
        }

        var items = await filtered
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .Select(item => new ReferenceValueDto(item.Id, item.Name, item.Unit, item.Value, item.AbsoluteTolerance))
            .ToListAsync(cancellationToken);

        return new PagedResult<ReferenceValueDto>(items, totalCount, query.Page, pageSize);
    }

    /// <inheritdoc />
    public async Task<ReferenceValueDto> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        _ = ResourceValidation.UserId(currentUser);
        var item = await db.ReferenceValues.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw ResourceValidation.Missing();
        return new ReferenceValueDto(item.Id, item.Name, item.Unit, item.Value, item.AbsoluteTolerance);
    }

    /// <inheritdoc />
    public async Task<ReferenceValueDto> CreateAsync(
        CreateReferenceValueRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ResourceValidation.RequireRole(currentUser, nameof(UserRole.Administrator));
        Validate(request);

        var item = new ReferenceValue(request.Name, request.Unit, request.Value!.Value, request.AbsoluteTolerance!.Value);
        db.ReferenceValues.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return new ReferenceValueDto(item.Id, item.Name, item.Unit, item.Value, item.AbsoluteTolerance);
    }

    /// <inheritdoc />
    public async Task<ReferenceValueDto> UpdateAsync(
        int id, UpdateReferenceValueRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ResourceValidation.RequireRole(currentUser, nameof(UserRole.Administrator));
        Validate(request);

        var item = await db.ReferenceValues.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw ResourceValidation.Missing();

        // Одиниця еталона зашита в уже створені серії: конструктор Experiment перевіряв
        // збіг лише під час створення, тому зміна одиниці зламала б статистику старої серії.
        if (!string.Equals(item.Unit.Trim(), request.Unit.Trim(), StringComparison.Ordinal)
            && await db.Experiments.AnyAsync(experiment => experiment.ReferenceValueId == id, cancellationToken))
        {
            throw ResourceValidation.Conflict("Одиницю еталона, який використовується в серіях, змінювати не можна.");
        }

        item.Update(request.Name, request.Unit, request.Value!.Value, request.AbsoluteTolerance!.Value);
        await db.SaveChangesAsync(cancellationToken);
        return new ReferenceValueDto(item.Id, item.Name, item.Unit, item.Value, item.AbsoluteTolerance);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        ResourceValidation.RequireRole(currentUser, nameof(UserRole.Administrator));
        var item = await db.ReferenceValues.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw ResourceValidation.Missing();
        db.ReferenceValues.Remove(item);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // 409 лише для підтвердженого конфлікту FK: інші збої запису не маскуємо.
            if (await db.Experiments.AnyAsync(experiment => experiment.ReferenceValueId == id, cancellationToken))
            {
                throw ResourceValidation.Conflict("Еталон використовується в серіях вимірювань.");
            }

            throw;
        }
    }

    private static void Validate(object request)
    {
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true))
        {
            throw new ArgumentException(string.Join(" ", results.Select(result => result.ErrorMessage)), nameof(request));
        }
    }
}
