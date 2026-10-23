using HhAutoApplier.Application.Commands.Searches;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Application.Queries.Searches;
using Microsoft.AspNetCore.Mvc;

namespace HhAutoApplier.Api.Controllers;

[Tags("Поиски")]
[Route("api/searches")]
public class SearchesController : ApiController
{
    /// <summary>
    /// Сохранённые поиски с правилами отбора.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SearchDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromServices] GetSearchesQueryHandler handler, CancellationToken ct)
    {
        return Ok(await handler.Handle(new GetSearchesQuery(), ct));
    }

    /// <summary>
    /// Создать поиск или обновить существующий с тем же именем.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(SearchDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Save(
        [FromBody] SaveSearchCommand command,
        [FromServices] SaveSearchCommandHandler handler,
        CancellationToken ct
    )
    {
        return FromResult((await handler.Handle(command, ct)).Value);
    }

    /// <summary>
    /// Удалить поиск.
    /// </summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(NotFoundError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, [FromServices] DeleteSearchCommandHandler handler, CancellationToken ct)
    {
        return FromResult((await handler.Handle(new DeleteSearchCommand(id), ct)).Value);
    }
}
