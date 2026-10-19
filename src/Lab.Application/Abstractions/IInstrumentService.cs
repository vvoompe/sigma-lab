using Lab.Contracts;
using Lab.Contracts.Instruments;

namespace Lab.Application.Abstractions;

public interface IInstrumentService
{
    Task<PagedResult<InstrumentDto>> ListAsync(ResourcePageQuery query, CancellationToken cancellationToken = default);
    Task<InstrumentDto> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<InstrumentDto> CreateAsync(CreateInstrumentRequest request, CancellationToken cancellationToken = default);
    Task<InstrumentDto> RenameAsync(int id, RenameInstrumentRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
