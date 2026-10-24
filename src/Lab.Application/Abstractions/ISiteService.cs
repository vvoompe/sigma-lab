using Lab.Contracts;
using Lab.Contracts.Sites;

namespace Lab.Application.Abstractions;

/// <summary>Результат оновлення майданчика.</summary>
public enum SiteUpdateStatus
{
    /// <summary>Майданчик оновлено.</summary>
    Updated,

    /// <summary>Майданчик не знайдено.</summary>
    NotFound,

    /// <summary>Майданчик належить іншому користувачу.</summary>
    Forbidden,
}

/// <summary>Результат призначення керівника лабораторії на майданчик.</summary>
public enum SiteManagerAssignmentStatus
{
    /// <summary>Призначення збережене (або вже було таким самим).</summary>
    Assigned,

    /// <summary>Майданчик не знайдено.</summary>
    SiteNotFound,

    /// <summary>Дію дозволено лише адміністратору.</summary>
    Forbidden,

    /// <summary>Користувача з таким ідентифікатором не знайдено.</summary>
    ManagerNotFound,

    /// <summary>Користувач існує, але не має ролі <c>LabManager</c>.</summary>
    ManagerRoleMissing,
}

/// <summary>Операції над майданчиками (зона Z1 розширює цей сервіс своїми сценаріями).</summary>
public interface ISiteService
{
    /// <summary>Повертає сторінку майданчиків за фільтрами, відсортовану за назвою й Id.</summary>
    Task<PagedResult<SiteDto>> ListAsync(SiteQuery query, CancellationToken cancellationToken = default);

    /// <summary>Повертає майданчик за ідентифікатором або <c>null</c>.</summary>
    Task<SiteDto?> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Створює майданчик і повертає його контракт.</summary>
    Task<SiteDto> CreateAsync(CreateSiteRequest request, CancellationToken cancellationToken = default);

    /// <summary>Оновлює майданчик власника або повертає його статус доступу.</summary>
    Task<SiteUpdateStatus> UpdateAsync(
        int id,
        UpdateSiteRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Закріплює керівника лабораторії за майданчиком. Дозволено лише адміністратору,
    /// цільовий користувач повинен мати роль <c>LabManager</c>; зміна пишеться в аудит.
    /// </summary>
    Task<SiteManagerAssignmentStatus> AssignManagerAsync(
        int id,
        AssignSiteManagerRequest request,
        CancellationToken cancellationToken = default);
}
