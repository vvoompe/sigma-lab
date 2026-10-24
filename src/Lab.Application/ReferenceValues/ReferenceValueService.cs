using System.ComponentModel.DataAnnotations;
using Lab.Application.Abstractions;
using Lab.Application.Resources;
using Lab.Application.Security;
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
    public async Task<IReadOnlyList<ReferenceValueDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        _ = ResourceValidation.UserId(currentUser);
        return await db.ReferenceValues
            .AsNoTracking()
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Id)
            .Select(item => new ReferenceValueDto(item.Id, item.Name, item.Unit, item.Value, item.AbsoluteTolerance))
            .ToListAsync(cancellationToken);
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
            // FK experiment.ReferenceValueId налаштований з Restrict: еталон у серіях не видаляємо.
            throw ResourceValidation.Conflict("Еталон використовується в серіях вимірювань.");
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
