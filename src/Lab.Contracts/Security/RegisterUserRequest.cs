using System.ComponentModel.DataAnnotations;

namespace Lab.Contracts.Security;

/// <summary>Дані для створення профілю та облікових даних дослідника.</summary>
public sealed class RegisterUserRequest
{
    /// <summary>Ім'я користувача.</summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    /// <summary>Унікальна адреса електронної пошти.</summary>
    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; init; } = string.Empty;

    /// <summary>Пароль облікового запису (12–128 символів).</summary>
    [Required]
    [StringLength(128, MinimumLength = 12)]
    public string Password { get; init; } = string.Empty;
}
