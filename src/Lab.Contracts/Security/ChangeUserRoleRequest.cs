using System.ComponentModel.DataAnnotations;

namespace Lab.Contracts.Security;

/// <summary>Запит адміністратора на зміну ролі іншого користувача.</summary>
public sealed class ChangeUserRoleRequest
{
    /// <summary>Нова роль: Researcher, LabManager або Administrator.</summary>
    [Required]
    [StringLength(32, MinimumLength = 1)]
    public string Role { get; init; } = string.Empty;
}
