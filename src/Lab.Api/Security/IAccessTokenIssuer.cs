using Lab.Application.Security;
using Lab.Contracts.Security;

namespace Lab.Api.Security;

/// <summary>Створює короткоживучі токени доступу для автентифікованих профілів.</summary>
public interface IAccessTokenIssuer
{
    /// <summary>Видає токен для профілю та повертає безпечну відповідь API.</summary>
    AccessTokenResponse Create(AuthenticatedUser user);
}
