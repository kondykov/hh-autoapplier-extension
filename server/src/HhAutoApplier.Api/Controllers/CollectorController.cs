using HhAutoApplier.Application.Commands.Collecting;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Application.Queries.Collecting;
using HhAutoApplier.Domain.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HhAutoApplier.Api.Controllers;

[Tags("Сбор")]
[Route("api/collect")]
// Общие операции над данными всех пользователей — только Admin
[Authorize(Roles = Roles.Admin)]
public class CollectorController : ApiController
{
    /// <summary>
    /// Выполнить сбор и дождаться итога: все включённые поиски по всем источникам. Может идти несколько минут, обрыв запроса прерывает сбор.
    /// </summary>
    /// <response code="409">Сбор уже идёт.</response>
    /// <response code="502">Источник заблокировал сбор (капча, 403, 429); найденное до этого сохранено.</response>
    [HttpPost]
    [ProducesResponseType(typeof(CollectResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ConflictError), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(SourceUnavailableError), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Collect([FromServices] CollectVacanciesCommandHandler handler, CancellationToken ct)
    {
        return FromResult((await handler.Handle(new CollectVacanciesCommand(CollectionProgress.Api), ct)).Value);
    }

    /// <summary>
    /// Запустить сбор в фоне и сразу ответить: проход не зависит от запроса и страницы.
    /// </summary>
    /// <response code="409">Сбор уже идёт.</response>
    [HttpPost("start")]
    [ProducesResponseType(typeof(CollectionStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ConflictError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Start([FromServices] StartCollectionCommandHandler handler, CancellationToken ct)
    {
        return FromResult((await handler.Handle(new StartCollectionCommand(), ct)).Value);
    }

    /// <summary>
    /// Идущий проход сбора и итог последнего с момента старта сервера.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(CollectionStateDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Status([FromServices] GetCollectionStateQueryHandler handler, CancellationToken ct)
    {
        return Ok(await handler.Handle(new GetCollectionStateQuery(), ct));
    }
}
