using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using Microsoft.AspNetCore.Mvc;
using OneOf.Types;

namespace HhAutoApplier.Api.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class ApiController : ControllerBase
{
    // Результат сценария → HTTP. Разбор по типу значения: сейчас это OneOf.Value, на .NET 11 — значение union-типа,
    // поэтому контроллеры при переезде не меняются
    protected IActionResult FromResult(object? value) => value switch
    {
        NotFoundError e => NotFound(e),
        ConflictError e => Conflict(e),
        ValidationError e => BadRequest(e),
        UnauthorizedError e => Unauthorized(e),
        ForbiddenError e => StatusCode(StatusCodes.Status403Forbidden, e),
        SourceUnavailableError e => StatusCode(StatusCodes.Status502BadGateway, e),
        LlmUnavailableError e => StatusCode(StatusCodes.Status503ServiceUnavailable, e),
        FileDto f => File(f.Content, f.ContentType, f.FileName),
        Success or null => NoContent(),
        _ => Ok(value),
    };
}
