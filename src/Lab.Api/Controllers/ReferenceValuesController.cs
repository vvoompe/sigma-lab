using System.ComponentModel.DataAnnotations;
using Lab.Application.Abstractions;
using Lab.Contracts;
using Lab.Contracts.ReferenceValues;
using Microsoft.AspNetCore.Mvc;

namespace Lab.Api.Controllers;

/// <summary>Еталони: читання автентифікованим, зміни - лише адміністратор.</summary>
[ApiController]
[Route("api/reference-values")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
public sealed class ReferenceValuesController(IReferenceValueService values) : ControllerBase
{
    /// <summary>Повертає сторінку еталонів, відсортовану за назвою.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<ReferenceValueDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ReferenceValueDto>>> ListAsync(
        [FromQuery] ResourcePageQuery query, CancellationToken cancellationToken) =>
        Ok(await values.ListAsync(query, cancellationToken));

    /// <summary>Повертає еталон за ідентифікатором.</summary>
    [HttpGet("{id:int}", Name = "GetReferenceValueById")]
    [ProducesResponseType<ReferenceValueDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ReferenceValueDto>> GetAsync(
        [Range(1, int.MaxValue)] int id, CancellationToken cancellationToken) =>
        Ok(await values.GetAsync(id, cancellationToken));

    /// <summary>Створює еталон (лише адміністратор).</summary>
    [HttpPost]
    [ProducesResponseType<ReferenceValueDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ReferenceValueDto>> CreateAsync(
        [FromBody] CreateReferenceValueRequest request, CancellationToken cancellationToken)
    {
        var created = await values.CreateAsync(request, cancellationToken);
        return CreatedAtRoute("GetReferenceValueById", new { id = created.Id }, created);
    }

    /// <summary>Оновлює назву, значення й допуск (лише адміністратор); зміна одиниці повертає 409.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType<ReferenceValueDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ReferenceValueDto>> UpdateAsync(
        [Range(1, int.MaxValue)] int id,
        [FromBody] UpdateReferenceValueRequest request,
        CancellationToken cancellationToken) =>
        Ok(await values.UpdateAsync(id, request, cancellationToken));

    /// <summary>Видаляє еталон, якщо він не використовується в серіях.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAsync(
        [Range(1, int.MaxValue)] int id, CancellationToken cancellationToken)
    {
        await values.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
