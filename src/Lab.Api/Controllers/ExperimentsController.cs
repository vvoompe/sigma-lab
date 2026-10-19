using System.ComponentModel.DataAnnotations;
using Lab.Application.Abstractions;
using Lab.Contracts;
using Lab.Contracts.Experiments;
using Microsoft.AspNetCore.Mvc;

namespace Lab.Api.Controllers;

/// <summary>Власні серії дослідника; підпис і доступ менеджера інтегруються окремо Z3.</summary>
[ApiController]
[Route("api/experiments")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
public sealed class ExperimentsController(IExperimentService experiments) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<ExperimentDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ExperimentDto>>> ListAsync(
        [FromQuery] ExperimentQuery query, CancellationToken cancellationToken) =>
        Ok(await experiments.ListAsync(query, cancellationToken));

    [HttpGet("{id:int}", Name = "GetExperimentById")]
    [ProducesResponseType<ExperimentDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ExperimentDto>> GetAsync(
        [Range(1, int.MaxValue)] int id, CancellationToken cancellationToken) =>
        Ok(await experiments.GetAsync(id, cancellationToken));

    [HttpPost]
    [ProducesResponseType<ExperimentDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ExperimentDto>> CreateAsync(
        [FromBody] CreateExperimentRequest request, CancellationToken cancellationToken)
    {
        var item = await experiments.CreateAsync(request, cancellationToken);
        return CreatedAtRoute("GetExperimentById", new { id = item.Id }, item);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType<ExperimentDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ExperimentDto>> RenameAsync(
        [Range(1, int.MaxValue)] int id, [FromBody] RenameExperimentRequest request, CancellationToken cancellationToken) =>
        Ok(await experiments.RenameAsync(id, request, cancellationToken));

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAsync(
        [Range(1, int.MaxValue)] int id,
        [FromQuery, Required, Range(typeof(long), "1", "9223372036854775807")] long? expectedVersion,
        CancellationToken cancellationToken)
    {
        await experiments.DeleteAsync(id, expectedVersion ?? throw new ArgumentException("Потрібна expectedVersion."), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:int}/measurements")]
    [ProducesResponseType<MeasurementMutationDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<MeasurementMutationDto>> AddMeasurementAsync(
        [Range(1, int.MaxValue)] int id, [FromBody] AddMeasurementRequest request, CancellationToken cancellationToken)
    {
        var result = await experiments.AddMeasurementAsync(id, request, cancellationToken);
        return CreatedAtRoute("GetMeasurementById", new { id, measurementId = result.Measurement.Id }, result);
    }

    [HttpGet("{id:int}/measurements/{measurementId:int}", Name = "GetMeasurementById")]
    [ProducesResponseType<MeasurementDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MeasurementDto>> GetMeasurementAsync(
        [Range(1, int.MaxValue)] int id, [Range(1, int.MaxValue)] int measurementId, CancellationToken cancellationToken) =>
        Ok(await experiments.GetMeasurementAsync(id, measurementId, cancellationToken));

    [HttpGet("{id:int}/measurements")]
    [ProducesResponseType<PagedResult<MeasurementDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<MeasurementDto>>> ListMeasurementsAsync(
        [Range(1, int.MaxValue)] int id, [FromQuery] ResourcePageQuery query, CancellationToken cancellationToken) =>
        Ok(await experiments.ListMeasurementsAsync(id, query, cancellationToken));

    [HttpGet("{id:int}/statistics")]
    [ProducesResponseType<ExperimentStatisticsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ExperimentStatisticsDto>> StatisticsAsync(
        [Range(1, int.MaxValue)] int id, [FromQuery] StatisticsQuery query, CancellationToken cancellationToken) =>
        Ok(await experiments.StatisticsAsync(id, query, cancellationToken));
}
