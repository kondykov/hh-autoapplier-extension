using HhAutoApplier.Application.Commands.Resumes;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Application.Queries.Resumes;
using Microsoft.AspNetCore.Mvc;

namespace HhAutoApplier.Api.Controllers;

[Tags("Резюме")]
[Route("api/resumes")]
public class ResumesController : ApiController
{
    /// <summary>
    /// Профили резюме: подстрока для выбора на hh.ru и ключевые слова для автовыбора.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ResumeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromServices] GetResumesQueryHandler handler, CancellationToken ct)
    {
        return Ok(await handler.Handle(new GetResumesQuery(), ct));
    }

    /// <summary>
    /// Создать профиль резюме или обновить существующий с тем же именем.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(ResumeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Save(
        [FromBody] SaveResumeCommand command,
        [FromServices] SaveResumeCommandHandler handler,
        CancellationToken ct
    )
    {
        return FromResult((await handler.Handle(command, ct)).Value);
    }
}
