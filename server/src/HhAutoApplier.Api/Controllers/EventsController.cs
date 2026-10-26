using HhAutoApplier.Application.Commands.Notifications;
using HhAutoApplier.Application.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace HhAutoApplier.Api.Controllers;

[Tags("События")]
[Route("api/events")]
public class EventsController : ApiController
{
    /// <summary>
    /// Событие расширения (капча, дневной лимит) — в Telegram владельцу. notified = false — некому доставить или уже сообщено.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ReportEventResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Report(
        [FromBody] ReportEventCommand command,
        [FromServices] ReportEventCommandHandler handler,
        CancellationToken ct
    )
    {
        return Ok(await handler.Handle(command, ct));
    }
}
