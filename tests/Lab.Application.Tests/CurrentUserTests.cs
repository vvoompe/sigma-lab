using System.Security.Claims;
using Lab.Api.Security;
using Microsoft.AspNetCore.Http;

namespace Lab.Application.Tests;

public sealed class CurrentUserTests
{
    [Fact]
    public void Ідентифікатор_і_роль_читаються_з_перевірених_claims()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", "42"),
                new Claim("role", LabRoles.LabManager),
            ], "Bearer")),
        };
        var currentUser = new ClaimsCurrentUser(new HttpContextAccessor { HttpContext = context });

        Assert.Equal(42, currentUser.Id);
        Assert.Equal(LabRoles.LabManager, currentUser.Role);
    }

    [Fact]
    public void Некоректний_sub_не_стає_ідентифікатором()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "42x")], "Bearer")),
        };
        var currentUser = new ClaimsCurrentUser(new HttpContextAccessor { HttpContext = context });

        Assert.Null(currentUser.Id);
    }
}
