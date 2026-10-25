using HhAutoApplier.Application.Commands.Llm;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using Microsoft.AspNetCore.Mvc;

namespace HhAutoApplier.Api.Controllers;

[Tags("LLM")]
[Route("api/llm")]
public class LlmController : ApiController
{
    /// <summary>
    /// Проверить ключ и модель LLM коротким запросом к провайдеру (расходует один запрос дневного лимита).
    /// </summary>
    /// <response code="503">LLM не настроена, исчерпан дневной лимит или провайдер не отвечает.</response>
    [HttpPost("check")]
    [ProducesResponseType(typeof(LlmCheckDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(LlmUnavailableError), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Check([FromServices] CheckLlmCommandHandler handler, CancellationToken ct)
    {
        return FromResult((await handler.Handle(new CheckLlmCommand(), ct)).Value);
    }

    /// <summary>
    /// Оценить LLM вакансии, прошедшие правила (Pending): подходит — в очередь, сомнительно — в очередь с пометкой, не подходит — Skipped.
    /// Каждая вакансия — один запрос дневного лимита; при исчерпании оставшиеся ждут следующего запуска.
    /// </summary>
    /// <response code="503">LLM не настроена.</response>
    [HttpPost("evaluate")]
    [ProducesResponseType(typeof(LlmEvaluationResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(LlmUnavailableError), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Evaluate(
        [FromBody] EvaluatePendingVacanciesCommand command,
        [FromServices] EvaluatePendingVacanciesCommandHandler handler,
        CancellationToken ct)
    {
        return FromResult((await handler.Handle(command, ct)).Value);
    }

    /// <summary>
    /// Ответить LLM на анкеты работодателей (Questionnaire), которые прислало расширение: с ответами — в очередь,
    /// неполный ответ или вариант не из анкеты — Skipped с причиной. Каждая вакансия — один запрос дневного лимита.
    /// </summary>
    /// <response code="503">LLM не настроена.</response>
    [HttpPost("answers")]
    [ProducesResponseType(typeof(QuestionnaireAnswersResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(LlmUnavailableError), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Answers(
        [FromBody] AnswerQuestionnairesCommand command,
        [FromServices] AnswerQuestionnairesCommandHandler handler,
        CancellationToken ct)
    {
        return FromResult((await handler.Handle(command, ct)).Value);
    }

    /// <summary>
    /// Написать LLM сопроводительные письма для вакансий в очереди без письма: по резюме, выбранному для вакансии, и его шаблону письма.
    /// Письмо уходит в расширение вместе с очередью; каждая вакансия — один запрос дневного лимита.
    /// </summary>
    /// <response code="503">LLM не настроена.</response>
    [HttpPost("letters")]
    [ProducesResponseType(typeof(CoverLettersResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(LlmUnavailableError), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Letters(
        [FromBody] WriteCoverLettersCommand command,
        [FromServices] WriteCoverLettersCommandHandler handler,
        CancellationToken ct)
    {
        return FromResult((await handler.Handle(command, ct)).Value);
    }
}
