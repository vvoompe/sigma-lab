using Lab.Api.Security;
using Lab.Application.Security;
using Lab.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lab.Api.Controllers;

/// <summary>Реєстрація та вхід до лабораторної системи.</summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public sealed class AuthController(IAuthService auth, IAccessTokenIssuer tokenIssuer) : ControllerBase
{
    /// <summary>Реєструє дослідника та видає токен доступу.</summary>
    [HttpPost("register")]
    [ProducesResponseType<AccessTokenResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AccessTokenResponse>> RegisterAsync(
        [FromBody] RegisterUserRequest request,
        CancellationToken cancellationToken)
    {
        var user = await auth.RegisterAsync(request, cancellationToken);
        if (user is null)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Email вже зареєстровано.",
                detail: "Використайте іншу адресу електронної пошти.");
        }

        return StatusCode(StatusCodes.Status201Created, tokenIssuer.Create(user));
    }

    /// <summary>Перевіряє облікові дані та видає токен доступу.</summary>
    [HttpPost("login")]
    [ProducesResponseType<AccessTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AccessTokenResponse>> LoginAsync(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var user = await auth.LoginAsync(request, cancellationToken);
        if (user is null)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Не вдалося увійти.",
                Detail = "Невірна електронна пошта або пароль.",
            });
        }

        return Ok(tokenIssuer.Create(user));
    }
}
