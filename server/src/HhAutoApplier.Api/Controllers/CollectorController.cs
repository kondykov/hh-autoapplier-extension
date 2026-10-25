using HhAutoApplier.Application.Commands.Collecting;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
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
    /// Запустить сбор вакансий сейчас: все включённые поиски по всем источникам. Может идти несколько минут.
    /// </summary>
    /// <response code="502">Источник заблокировал сбор (капча, 403, 429); найденное до этого сохранено.</response>
    [HttpPost]
    [ProducesResponseType(typeof(CollectResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SourceUnavailableError), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Collect([FromServices] CollectVacanciesCommandHandler handler, CancellationToken ct)
    {
        return FromResult((await handler.Handle(new CollectVacanciesCommand(), ct)).Value);
    }
}
