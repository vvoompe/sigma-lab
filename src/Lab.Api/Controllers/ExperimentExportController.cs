using System.ComponentModel.DataAnnotations;
using Lab.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Lab.Api.Controllers;

[ApiController]
[Route("api/experiments")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
public sealed class ExperimentExportController(IExperimentExportService export) : ControllerBase
{
    [HttpGet("{id:int}/export.csv")]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK, "text/csv")]
    public async Task<IActionResult> CsvAsync([Range(1, int.MaxValue)] int id, CancellationToken cancellationToken) =>
        File(await export.ExportCsvAsync(id, cancellationToken), "text/csv; charset=utf-8", $"experiment-{id}.csv");
}
