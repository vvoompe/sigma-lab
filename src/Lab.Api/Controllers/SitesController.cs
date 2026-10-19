using Lab.Application.Abstractions;
using Lab.Contracts;
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
    /// <summary>Повертає сторінку майданчиків із фільтрами назви та координат.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<SiteDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<SiteDto>>> ListAsync(
        [FromQuery] SiteQuery query,
        CancellationToken cancellationToken) =>
        Ok(await sites.ListAsync(query, cancellationToken));

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

    /// <summary>Оновлює майданчик, якщо він належить поточному користувачу.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateAsync(
        int id,
        [FromBody] UpdateSiteRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sites.UpdateAsync(id, request, cancellationToken);
        return result switch
        {
            SiteUpdateStatus.Updated => NoContent(),
            SiteUpdateStatus.NotFound => NotFound(),
            SiteUpdateStatus.Forbidden => Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Немає прав на зміну майданчика."),
            _ => throw new InvalidOperationException("Невідомий результат оновлення майданчика."),
        };
    }
}
