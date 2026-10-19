using Lab.Application.Abstractions;
using Lab.Application.Security;
using Lab.Contracts;
using Lab.Contracts.Instruments;
using Lab.Domain.Instruments;
using Lab.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Lab.Application.Resources;

public sealed class InstrumentService(IAppDbContext db, ICurrentUser currentUser) : IInstrumentService
{
    public async Task<PagedResult<InstrumentDto>> ListAsync(ResourcePageQuery query, CancellationToken cancellationToken = default)
    {
        ResourceValidation.Validate(query);
        var items = Readable();
        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            var name = query.Name.Trim();
            items = items.Where(item => item.Name.Contains(name));
        }
        var count = await items.CountAsync(cancellationToken);
        var size = Math.Min(query.PageSize, ResourcePageQuery.MaxPageSize);
        var offset = (query.Page - 1L) * size;
        var page = offset >= count ? [] : await items.OrderBy(item => item.Name).ThenBy(item => item.Id)
            .Skip((int)offset).Take(size).ToListAsync(cancellationToken);
        return new PagedResult<InstrumentDto>(page.Select(ToDto).ToArray(), count, query.Page, size);
    }

    public async Task<InstrumentDto> GetAsync(int id, CancellationToken cancellationToken = default) =>
        ToDto(await Readable().SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw ResourceValidation.Missing());

    public async Task<InstrumentDto> CreateAsync(CreateInstrumentRequest request, CancellationToken cancellationToken = default)
    {
        ResourceValidation.RequireRole(currentUser, nameof(UserRole.Administrator));
        ResourceValidation.Validate(request);
        var instrument = new Instrument(ResourceValidation.RequiredValue(request.OwnerId), request.Name, request.SerialNumber,
            request.Unit, ResourceValidation.RequiredValue(request.MinValue), ResourceValidation.RequiredValue(request.MaxValue), ResourceValidation.RequiredValue(request.AbsoluteError));
        if (!await db.Users.AnyAsync(user => user.Id == instrument.OwnerId, cancellationToken))
            throw ResourceValidation.Missing();
        if (await db.Instruments.AnyAsync(item => item.SerialNumber == instrument.SerialNumber, cancellationToken))
            throw ResourceValidation.Conflict("SerialNumber вже використовується.");
        db.Instruments.Add(instrument);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            if (await db.Instruments.AnyAsync(item => item.SerialNumber == instrument.SerialNumber, cancellationToken))
                throw ResourceValidation.Conflict("SerialNumber вже використовується.");
            if (!await db.Users.AnyAsync(user => user.Id == instrument.OwnerId, cancellationToken))
                throw ResourceValidation.Conflict("Власника видалено паралельним запитом.");
            throw;
        }
        return ToDto(instrument);
    }

    public async Task<InstrumentDto> RenameAsync(int id, RenameInstrumentRequest request, CancellationToken cancellationToken = default)
    {
        ResourceValidation.RequireRole(currentUser, nameof(UserRole.Administrator));
        ResourceValidation.Validate(request);
        var instrument = await db.Instruments.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw ResourceValidation.Missing();
        instrument.Rename(request.Name);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(instrument);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        ResourceValidation.RequireRole(currentUser, nameof(UserRole.Administrator));
        var instrument = await db.Instruments.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw ResourceValidation.Missing();
        if (await db.Experiments.AnyAsync(item => item.InstrumentId == id, cancellationToken))
            throw ResourceValidation.Conflict("Прилад використовується в серіях.");
        db.Instruments.Remove(instrument);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            if (await db.Experiments.AnyAsync(item => item.InstrumentId == id, cancellationToken))
                throw ResourceValidation.Conflict("Прилад використовується в серіях.");
            throw;
        }
    }

    private IQueryable<Instrument> Readable()
    {
        var owner = ResourceValidation.UserId(currentUser);
        var items = db.Instruments.AsNoTracking();
        return currentUser.Role == nameof(UserRole.Administrator) ? items : items.Where(item => item.OwnerId == owner);
    }

    private static InstrumentDto ToDto(Instrument item) => new(item.Id, item.OwnerId, item.Name,
        item.SerialNumber, item.Unit, item.MinValue, item.MaxValue, item.AbsoluteError);
}
