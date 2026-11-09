using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Queries.About;
using Microsoft.AspNetCore.Mvc;

namespace HhAutoApplier.Api.Controllers;

[Tags("Версия")]
[Route("api/version")]
public class VersionController : ApiController
{
    /// <summary>
    /// Версия backend, коммит сборки и номер контракта API: по нему расширение проверяет совместимость.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(BuildVersionDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromServices] GetVersionQueryHandler handler, CancellationToken ct)
    {
        return Ok(await handler.Handle(new GetVersionQuery(), ct));
    }
}
