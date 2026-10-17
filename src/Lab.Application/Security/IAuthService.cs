using Lab.Contracts.Security;
using Lab.Domain.Security;

namespace Lab.Application.Security;

/// <summary>Результат автентифікації профілю.</summary>
public sealed record AuthenticatedUser(int Id, string Name, UserRole Role);

/// <summary>Результат адміністративної зміни ролі.</summary>
public enum RoleChangeStatus
{
    Changed,
    Unchanged,
    TargetNotFound,
    SelfChangeForbidden,
}

/// <summary>Операції реєстрації, входу та адміністрування ролей.</summary>
public interface IAuthService
{
    /// <summary>Створює профіль із роллю Researcher і хешем пароля.</summary>
    Task<AuthenticatedUser?> RegisterAsync(
        RegisterUserRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Перевіряє пароль і стан блокування; null означає невдалий вхід.</summary>
    Task<AuthenticatedUser?> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Змінює роль і записує подію аудиту атомарно.</summary>
    Task<RoleChangeStatus> ChangeRoleAsync(
        int actorId,
        int targetUserId,
        UserRole role,
        CancellationToken cancellationToken = default);
}
