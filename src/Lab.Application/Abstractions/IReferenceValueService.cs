using Lab.Contracts;
using Lab.Contracts.ReferenceValues;

namespace Lab.Application.Abstractions;

/// <summary>Еталони: читання для автентифікованих, зміни - лише адміністратор.</summary>
public interface IReferenceValueService
{
    /// <summary>Повертає обмежену сторінку еталонів, відсортовану за назвою.</summary>
    Task<PagedResult<ReferenceValueDto>> ListAsync(ResourcePageQuery query, CancellationToken cancellationToken = default);

    /// <summary>Повертає еталон або кидає 404.</summary>
    Task<ReferenceValueDto> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Створює еталон.</summary>
    Task<ReferenceValueDto> CreateAsync(CreateReferenceValueRequest request, CancellationToken cancellationToken = default);

    /// <summary>Повністю оновлює еталон; одиницю використаного еталона змінити не можна.</summary>
    Task<ReferenceValueDto> UpdateAsync(int id, UpdateReferenceValueRequest request, CancellationToken cancellationToken = default);

    /// <summary>Видаляє еталон, якщо він не використовується в серіях.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
