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
}
