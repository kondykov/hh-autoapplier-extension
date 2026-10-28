using HhAutoApplier.Application.Commands.Tokens;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Application.Queries.Tokens;
using Microsoft.AspNetCore.Mvc;

namespace HhAutoApplier.Api.Controllers;

[Tags("Токены")]
[Route("api/tokens")]
public class TokensController : ApiController
{
    /// <summary>
    /// Свои персональные токены для расширения (без значений — только начало).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ApiTokenDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromServices] GetApiTokensQueryHandler handler, CancellationToken ct)
    {
        return Ok(await handler.Handle(new GetApiTokensQuery(), ct));
    }

    /// <summary>
    /// Выпустить токен: значение (hh_…) показывается один раз — вставьте его в поле «Токен» панели расширения.
    /// </summary>
    /// <response code="409">Вход по токену — выпуск только после входа по паролю.</response>
    [HttpPost]
    [ProducesResponseType(typeof(CreatedApiTokenDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ConflictError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateApiTokenCommand command,
        [FromServices] CreateApiTokenCommandHandler handler,
        CancellationToken ct
    )
    {
        return FromResult((await handler.Handle(command, ct)).Value);
    }

    /// <summary>
    /// Отозвать свой токен: запросы с ним сразу получают 401.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(NotFoundError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ConflictError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Revoke(Guid id, [FromServices] RevokeApiTokenCommandHandler handler, CancellationToken ct)
    {
        return FromResult((await handler.Handle(new RevokeApiTokenCommand(id), ct)).Value);
    }
}