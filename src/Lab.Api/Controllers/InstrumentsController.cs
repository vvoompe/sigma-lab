using System.ComponentModel.DataAnnotations;
using Lab.Application.Abstractions;
using Lab.Contracts;
using Lab.Contracts.Instruments;
using Microsoft.AspNetCore.Mvc;

namespace Lab.Api.Controllers;

/// <summary>Прилади: читання власником, керування адміністратором.</summary>
[ApiController]
[Route("api/instruments")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
public sealed class InstrumentsController(IInstrumentService instruments) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<InstrumentDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<InstrumentDto>>> ListAsync(
        [FromQuery] ResourcePageQuery query, CancellationToken cancellationToken) =>
        Ok(await instruments.ListAsync(query, cancellationToken));

    [HttpGet("{id:int}", Name = "GetInstrumentById")]
    [ProducesResponseType<InstrumentDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<InstrumentDto>> GetAsync(
        [Range(1, int.MaxValue)] int id, CancellationToken cancellationToken) =>
        Ok(await instruments.GetAsync(id, cancellationToken));

    [HttpPost]
    [ProducesResponseType<InstrumentDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<InstrumentDto>> CreateAsync(
        [FromBody] CreateInstrumentRequest request, CancellationToken cancellationToken)
    {
        var item = await instruments.CreateAsync(request, cancellationToken);
        return CreatedAtRoute("GetInstrumentById", new { id = item.Id }, item);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType<InstrumentDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<InstrumentDto>> RenameAsync(
        [Range(1, int.MaxValue)] int id, [FromBody] RenameInstrumentRequest request, CancellationToken cancellationToken) =>
        Ok(await instruments.RenameAsync(id, request, cancellationToken));

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAsync([Range(1, int.MaxValue)] int id, CancellationToken cancellationToken)
    {
        await instruments.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
