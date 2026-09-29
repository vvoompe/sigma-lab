using Lab.Application.Abstractions;
using Lab.Contracts.Sites;
using Microsoft.AspNetCore.Mvc;

namespace Lab.Api.Controllers;

/// <summary>
/// Майданчики. Приклад контролера, за яким зона Z2 додає решту ресурсів:
/// контролер не торкається бази даних, він викликає сервіс і перекладає результат у HTTP.
/// </summary>
/// <param name="sites">Сервіс майданчиків.</param>
[ApiController]
[Route("api/sites")]
public sealed class SitesController(ISiteService sites) : ControllerBase
{
    /// <summary>Повертає всі майданчики.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SiteDto>>> ListAsync(CancellationToken cancellationToken) =>
        Ok(await sites.ListAsync(cancellationToken));

    /// <summary>Повертає майданчик за ідентифікатором.</summary>
    [HttpGet("{id:int}", Name = "GetSiteById")]
    public async Task<ActionResult<SiteDto>> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var site = await sites.GetAsync(id, cancellationToken);
        return site is null ? NotFound() : Ok(site);
    }

    /// <summary>Створює майданчик.</summary>
    [HttpPost]
    public async Task<ActionResult<SiteDto>> CreateAsync(
        [FromBody] CreateSiteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var created = await sites.CreateAsync(request, cancellationToken);
        return CreatedAtRoute("GetSiteById", new { id = created.Id }, created);
    }
}
