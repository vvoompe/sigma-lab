using System.ComponentModel.DataAnnotations;
using Lab.Application.Abstractions;
using Lab.Contracts.Experiments;
using Microsoft.AspNetCore.Mvc;

namespace Lab.Api.Controllers;

[ApiController]
[Route("api/experiments/{id:int}/context")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
public sealed class ExperimentContextController(IExperimentContextService contexts) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ExperimentContextDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ExperimentContextDto>> GetAsync(
        [Range(1, int.MaxValue)] int id, CancellationToken cancellationToken) =>
        Ok(await contexts.GetAsync(id, cancellationToken));
}
