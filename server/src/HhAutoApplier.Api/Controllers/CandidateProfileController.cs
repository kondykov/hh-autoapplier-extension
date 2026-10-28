using HhAutoApplier.Application.Commands.Candidates;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Application.Queries.Candidates;
using Microsoft.AspNetCore.Mvc;

namespace HhAutoApplier.Api.Controllers;

[Tags("Профиль кандидата")]
[Route("api/profile")]
public class CandidateProfileController : ApiController
{
    /// <summary>
    /// Профиль кандидата для LLM (#35): общая часть и факты по навыкам. Под вакансию в промпт идут общая часть
    /// и факты только по её стеку. Профиля нет — пустой.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(CandidateProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ForbiddenError), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(Guid? userId, [FromServices] GetCandidateProfileQueryHandler handler, CancellationToken ct)
    {
        return FromResult((await handler.Handle(new GetCandidateProfileQuery(userId), ct)).Value);
    }

    /// <summary>
    /// Сохранить свой профиль кандидата целиком.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(CandidateProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Save(
        [FromBody] SaveCandidateProfileCommand command,
        [FromServices] SaveCandidateProfileCommandHandler handler,
        CancellationToken ct
    )
    {
        return FromResult((await handler.Handle(command, ct)).Value);
    }
}
