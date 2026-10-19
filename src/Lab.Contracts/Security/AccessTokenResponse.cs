namespace Lab.Contracts.Security;

/// <summary>Короткоживучий токен доступу та безпечні дані профілю.</summary>
public sealed record AccessTokenResponse(
    string AccessToken,
    string TokenType,
    DateTime ExpiresAtUtc,
    int UserId,
    string Name,
    string Role);
