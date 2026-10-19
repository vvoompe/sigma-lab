using System.ComponentModel.DataAnnotations;
using Lab.Application.Security;

namespace Lab.Application.Resources;

internal static class ResourceValidation
{
    internal static void Validate(object request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validator.ValidateObject(request, new ValidationContext(request), validateAllProperties: true);
    }

    internal static int UserId(ICurrentUser user) => user.Id is > 0 and var id
        ? id : throw new ResourceException(ResourceErrorKind.Unauthorized, "Потрібна автентифікація.");

    internal static T RequiredValue<T>(T? value) where T : struct =>
        value ?? throw new ValidationException("Обов'язкове поле відсутнє.");

    internal static void RequireRole(ICurrentUser user, string role)
    {
        _ = UserId(user);
        if (!string.Equals(user.Role, role, StringComparison.Ordinal))
            throw new ResourceException(ResourceErrorKind.Forbidden, "Немає прав на цю дію.");
    }

    internal static ResourceException Missing() => new(ResourceErrorKind.NotFound, "Ресурс не знайдено.");
    internal static ResourceException Conflict(string message) => new(ResourceErrorKind.Conflict, message);
}
