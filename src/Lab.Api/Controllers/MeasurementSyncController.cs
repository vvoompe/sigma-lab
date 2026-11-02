using Lab.Application.Abstractions;
using Lab.Contracts.Sync;
using Microsoft.AspNetCore.Mvc;

namespace Lab.Api.Controllers;

[ApiController]
[Route("api/sync/measurements")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public sealed class MeasurementSyncController(IMeasurementSyncService sync) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<MeasurementSyncBatchResult>(StatusCodes.Status207MultiStatus)]
    public async Task<ActionResult<MeasurementSyncBatchResult>> BatchAsync(
        [FromBody] MeasurementSyncBatchRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status207MultiStatus, await sync.SynchronizeBatchAsync(request, cancellationToken));

    [HttpPost("single")]
    [ProducesResponseType<MeasurementSyncResult>(StatusCodes.Status201Created)]
    [ProducesResponseType<MeasurementSyncResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MeasurementSyncResult>> SingleAsync(
        [FromBody] MeasurementSyncRequest request, CancellationToken cancellationToken)
    {
        var result = await sync.SynchronizeAsync(request, cancellationToken);
        if (result.Status < 400) return StatusCode(result.Status, result);
        var problem = new ProblemDetails { Status = result.Status, Title = result.Error!.Title };
        if (result.ExperimentVersion is { } version) problem.Extensions["experimentVersion"] = version;
        return new ObjectResult(problem) { StatusCode = result.Status, ContentTypes = { "application/problem+json" } };
    }
}
