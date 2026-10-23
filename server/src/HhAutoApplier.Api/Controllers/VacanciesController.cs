using HhAutoApplier.Application.Commands.Vacancies;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Application.Queries.Vacancies;
using HhAutoApplier.Domain.Vacancies;
using Microsoft.AspNetCore.Mvc;

namespace HhAutoApplier.Api.Controllers;

[Tags("Вакансии")]
[Route("api")]
public class VacanciesController : ApiController
{
    /// <summary>
    /// Очередь на отклик: лучшие вакансии с уже выбранным резюме.
    /// </summary>
    [HttpGet("queue")]
    [ProducesResponseType(typeof(IReadOnlyList<VacancyDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQueue(
        [FromQuery] GetQueueQuery query,
        [FromServices] GetQueueQueryHandler handler,
        CancellationToken ct
    )
    {
        return Ok(await handler.Handle(query, ct));
    }

    /// <summary>
    /// Список вакансий с фильтром по статусу и поиску, новые сверху.
    /// </summary>
    [HttpGet("vacancies")]
    [ProducesResponseType(typeof(IReadOnlyList<VacancyDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVacancies(
        [FromQuery] GetVacanciesQuery query,
        [FromServices] GetVacanciesQueryHandler handler,
        CancellationToken ct
    )
    {
        return Ok(await handler.Handle(query, ct));
    }

    /// <summary>
    /// Исход по вакансии от клиента: Applied, Skipped, Failed, Rejected или Queued (вернуть в очередь).
    /// </summary>
    /// <response code="404">Вакансия не найдена.</response>
    /// <response code="409">Переход статуса недопустим.</response>
    [HttpPost("vacancies/{id}/status")]
    [ProducesResponseType(typeof(VacancyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotFoundError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ConflictError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetStatus(
        string id,
        [FromBody] SetVacancyStatusRequest request,
        [FromServices] SetVacancyStatusCommandHandler handler,
        CancellationToken ct
    )
    {
        var command = new SetVacancyStatusCommand
        {
            VacancyId = id,
            Status = request.Status,
            Reason = request.Reason,
            Resume = request.Resume,
        };
        return FromResult((await handler.Handle(command, ct)).Value);
    }

    /// <summary>
    /// Число вакансий в каждом статусе.
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(IReadOnlyDictionary<string, int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats([FromServices] GetStatsQueryHandler handler, CancellationToken ct)
    {
        return Ok(await handler.Handle(new GetStatsQuery(), ct));
    }

    /// <summary>
    /// Принять вакансии от клиента и оценить их правилами поиска. Уже известные пропускаются.
    /// </summary>
    /// <response code="400">Поиск с таким именем не найден.</response>
    [HttpPost("import")]
    [ProducesResponseType(typeof(ImportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Import(
        [FromBody] ImportVacanciesCommand command,
        [FromServices] ImportVacanciesCommandHandler handler,
        CancellationToken ct
    )
    {
        return FromResult((await handler.Handle(command, ct)).Value);
    }
}

public record SetVacancyStatusRequest(VacancyStatus Status, string? Reason, string? Resume);
