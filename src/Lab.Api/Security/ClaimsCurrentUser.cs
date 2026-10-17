using System.Globalization;
using System.Security.Claims;
using Lab.Application.Security;

namespace Lab.Api.Security;

/// <summary>Поточний користувач, побудований із перевірених claims HTTP-запиту.</summary>
public sealed class ClaimsCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    /// <inheritdoc />
    public int? Id
    {
        get
        {
            var value = Principal?.FindFirstValue("sub")
                ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                ? id
                : null;
        }
    }

    /// <inheritdoc />
    public string? Role =>
        Principal?.FindFirstValue("role")
        ?? Principal?.FindFirstValue(ClaimTypes.Role);
}
