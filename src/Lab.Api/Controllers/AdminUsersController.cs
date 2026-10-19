using Lab.Api.Security;
using Lab.Application.Security;
using Lab.Contracts.Security;
using Lab.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace Lab.Api.Controllers;

/// <summary>Адміністративне керування ролями користувачів.</summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = LabRoles.Administrator)]
public sealed class AdminUsersController(IAuthService auth, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Змінює роль користувача; власну роль змінити неможливо.</summary>
    [HttpPatch("{id:int}/role")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangeRoleAsync(
        [Range(1, int.MaxValue)]
        int id,
        [FromBody] ChangeUserRoleRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.GetNames<UserRole>().Contains(request.Role, StringComparer.Ordinal)
            || !Enum.TryParse<UserRole>(request.Role, ignoreCase: false, out var role)
            || !Enum.IsDefined(role))
        {
            ModelState.AddModelError(nameof(request.Role), "Невідома роль.");
            return ValidationProblem(ModelState);
        }

        if (currentUser.Id is not { } actorId)
        {
            return Forbid();
        }

        var result = await auth.ChangeRoleAsync(actorId, id, role, cancellationToken);
        return result switch
        {
            RoleChangeStatus.Changed or RoleChangeStatus.Unchanged => NoContent(),
            RoleChangeStatus.TargetNotFound => NotFound(),
            RoleChangeStatus.SelfChangeForbidden => Forbid(),
            _ => throw new InvalidOperationException("Невідомий результат зміни ролі."),
        };
    }
}
