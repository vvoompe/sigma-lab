using System.ComponentModel.DataAnnotations;

namespace Lab.Contracts.Security;

/// <summary>Дані для входу користувача.</summary>
public sealed class LoginRequest
{
    /// <summary>Адреса електронної пошти.</summary>
    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; init; } = string.Empty;

    /// <summary>Пароль облікового запису.</summary>
    [Required]
    [StringLength(128)]
    public string Password { get; init; } = string.Empty;
}
