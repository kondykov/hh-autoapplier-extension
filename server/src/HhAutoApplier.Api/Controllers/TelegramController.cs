using HhAutoApplier.Application.Commands.Telegram;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Application.Queries.Telegram;
using Microsoft.AspNetCore.Mvc;

namespace HhAutoApplier.Api.Controllers;

[Tags("Telegram")]
[Route("api/telegram")]
public class TelegramController : ApiController
{
    /// <summary>
    /// Свой чат Telegram: настроен ли бот на сервере и привязан ли чат.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(TelegramChatDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromServices] GetTelegramQueryHandler handler, CancellationToken ct)
    {
        return Ok(await handler.Handle(new GetTelegramQuery(), ct));
    }

    /// <summary>
    /// Код привязки чата (действует 15 минут): откройте ссылку или отправьте боту /start &lt;код&gt;. Прежний чат отвяжется, когда привяжется новый.
    /// </summary>
    /// <response code="409">Вход по токену или ключу — код только после входа по паролю; или бот не настроен.</response>
    [HttpPost("link")]
    [ProducesResponseType(typeof(TelegramLinkDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ConflictError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Link([FromServices] LinkTelegramCommandHandler handler, CancellationToken ct)
    {
        return FromResult((await handler.Handle(new LinkTelegramCommand(), ct)).Value);
    }

    /// <summary>
    /// Отвязать свой чат: уведомлений и команд больше не будет.
    /// </summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(NotFoundError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unlink([FromServices] UnlinkTelegramCommandHandler handler, CancellationToken ct)
    {
        return FromResult((await handler.Handle(new UnlinkTelegramCommand(), ct)).Value);
    }
}
