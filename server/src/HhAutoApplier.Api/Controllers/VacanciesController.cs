using System.ComponentModel.DataAnnotations;
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
    [ProducesResponseType(typeof(ForbiddenError), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetVacancies(
        [FromQuery] GetVacanciesQuery query,
        [FromServices] GetVacanciesQueryHandler handler,
        CancellationToken ct
    )
    {
        return FromResult((await handler.Handle(query, ct)).Value);
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
    /// Оценить заново отсеянную вакансию (Skipped): правила и затем LLM, импортная карточка — через подтверждение при сборе.
    /// Письмо LLM и ответы анкеты сбрасываются, письмо, поправленное человеком, остаётся.
    /// </summary>
    /// <response code="404">Вакансия не найдена.</response>
    /// <response code="409">Вакансия не отсеяна: оценить заново нельзя.</response>
    /// <response code="503">LLM не настроена, статус не меняется.</response>
    [HttpPost("vacancies/{id}/reevaluate")]
    [ProducesResponseType(typeof(VacancyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotFoundError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ConflictError), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(LlmUnavailableError), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Reevaluate(
        string id,
        [FromServices] ReevaluateVacancyCommandHandler handler,
        CancellationToken ct
    )
    {
        return FromResult((await handler.Handle(new ReevaluateVacancyCommand { VacancyId = id }, ct)).Value);
    }

    /// <summary>
    /// Скрыть отсеянную вакансию из списка: статус, история и счётчики не меняются.
    /// </summary>
    /// <response code="404">Вакансия не найдена.</response>
    /// <response code="409">Вакансия не отсеяна: скрыть нельзя.</response>
    [HttpPost("vacancies/{id}/hide")]
    [ProducesResponseType(typeof(VacancyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotFoundError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ConflictError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Hide(
        string id,
        [FromServices] SetVacancyHiddenCommandHandler handler,
        CancellationToken ct
    )
    {
        return FromResult((await handler.Handle(new SetVacancyHiddenCommand { VacancyId = id, Hidden = true }, ct)).Value);
    }

    /// <summary>
    /// Вернуть скрытую вакансию в список.
    /// </summary>
    /// <response code="404">Вакансия не найдена.</response>
    [HttpPost("vacancies/{id}/unhide")]
    [ProducesResponseType(typeof(VacancyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotFoundError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unhide(
        string id,
        [FromServices] SetVacancyHiddenCommandHandler handler,
        CancellationToken ct
    )
    {
        return FromResult((await handler.Handle(new SetVacancyHiddenCommand { VacancyId = id, Hidden = false }, ct)).Value);
    }

    /// <summary>
    /// Ручная проверка вакансии в статусе Review: одобрить — в очередь (coverLetter — поправленное письмо, пусто — письмо LLM;
    /// answers — поправленные ответы анкеты, все по порядку вопросов, пусто — ответы LLM),
    /// отклонить — Skipped с причиной. Список на проверке — GET /api/vacancies?status=Review, страница — веб-интерфейс, «Проверка» (/#review).
    /// </summary>
    /// <response code="404">Вакансия не найдена.</response>
    /// <response code="409">Вакансия не на проверке.</response>
    [HttpPost("vacancies/{id}/review")]
    [ProducesResponseType(typeof(VacancyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotFoundError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ConflictError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Review(
        string id,
        [FromBody] ReviewVacancyRequest request,
        [FromServices] ReviewVacancyCommandHandler handler,
        CancellationToken ct
    )
    {
        var command = new ReviewVacancyCommand { VacancyId = id, Approve = request.Approve, CoverLetter = request.CoverLetter, Answers = request.Answers, Reason = request.Reason };
        return FromResult((await handler.Handle(command, ct)).Value);
    }

    /// <summary>
    /// Отклик на проверке с просьбами работодателя к письму: подтвердить кодовые слова (confirmCodeWords — номера просьб)
    /// и подставить значения из профиля. Блок значений в письме обновляется, статус не меняется.
    /// </summary>
    /// <response code="404">Вакансия не найдена.</response>
    /// <response code="409">Вакансия не на проверке, просьб нет или такую просьбу подтвердить нельзя.</response>
    [HttpPost("vacancies/{id}/letter-requests/resolve")]
    [ProducesResponseType(typeof(VacancyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotFoundError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ConflictError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResolveLetterRequests(
        string id,
        [FromBody] ResolveLetterRequestsRequest request,
        [FromServices] ResolveLetterRequestsCommandHandler handler,
        CancellationToken ct
    )
    {
        return FromResult((await handler.Handle(new ResolveLetterRequestsCommand { VacancyId = id, ConfirmCodeWords = request.ConfirmCodeWords ?? [] }, ct)).Value);
    }

    /// <summary>
    /// Анкета работодателя от расширения: вопросы ждут ответов LLM (статус Questionnaire), затем вакансия вернётся в очередь с ответами.
    /// Прежние ответы сбрасываются — анкета могла поменяться.
    /// </summary>
    /// <response code="404">Вакансия не найдена.</response>
    /// <response code="409">Отклик уже отправлен.</response>
    [HttpPost("vacancies/{id}/questionnaire")]
    [ProducesResponseType(typeof(VacancyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotFoundError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ConflictError), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ValidationError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SubmitQuestionnaire(
        string id,
        [FromBody] SubmitQuestionnaireRequest request,
        [FromServices] SubmitQuestionnaireCommandHandler handler,
        CancellationToken ct
    )
    {
        var command = new SubmitQuestionnaireCommand { VacancyId = id, Questions = request.Questions };
        return FromResult((await handler.Handle(command, ct)).Value);
    }

    /// <summary>
    /// Число вакансий в каждом статусе.
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(IReadOnlyDictionary<string, int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ForbiddenError), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetStats(Guid? userId, [FromServices] GetStatsQueryHandler handler, CancellationToken ct)
    {
        return FromResult((await handler.Handle(new GetStatsQuery(userId), ct)).Value);
    }

    /// <summary>
    /// Принять вакансии от клиента. Уже известные пропускаются.
    /// Вакансия, которую источник ещё не подтвердил, ждёт (waiting): с выдачи приходит неполная карточка,
    /// отклик оценится правилами поиска после подтверждения по странице вакансии при сборе.
    /// </summary>
    /// <response code="400">Поиск с таким именем не найден.</response>
    /// <response code="409">Те же вакансии одновременно добавил сбор — повторите импорт.</response>
    [HttpPost("import")]
    [ProducesResponseType(typeof(ImportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ConflictError), StatusCodes.Status409Conflict)]
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

public record ResolveLetterRequestsRequest([MaxLength(10)] List<int>? ConfirmCodeWords = null);

public record SubmitQuestionnaireRequest([Required, MinLength(1), MaxLength(50)] List<QuestionInput> Questions);

public record ReviewVacancyRequest(bool Approve, [MaxLength(4000)] string? CoverLetter, [MaxLength(500)] string? Reason,
    [MaxLength(50)] List<List<string>>? Answers = null);
