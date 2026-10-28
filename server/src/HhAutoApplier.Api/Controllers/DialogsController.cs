using System.ComponentModel.DataAnnotations;
using HhAutoApplier.Application.Commands.Dialogs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Application.Queries.Dialogs;
using Microsoft.AspNetCore.Mvc;

namespace HhAutoApplier.Api.Controllers;

[Tags("Диалоги")]
[Route("api/dialogs")]
public class DialogsController : ApiController
{
    /// <summary>
    /// Сообщения работодателей с разбором LLM, новые сверху; handled=false — необработанные. Страница — веб-интерфейс, «Ответы работодателей» (/#dialogs).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<EmployerMessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ForbiddenError), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(
        [FromQuery] GetDialogsQuery query,
        [FromServices] GetDialogsQueryHandler handler,
        CancellationToken ct
    )
    {
        return FromResult((await handler.Handle(query, ct)).Value);
    }

    /// <summary>
    /// Чаты откликов от расширения: последнее сообщение и состояние отклика hh.ru. Отказ → Rejected, приглашение → Invited;
    /// сообщения работодателей (кроме отказов) разбирает LLM в фоне. В ответе — чаты, где ждёт автоответ на отказ.
    /// </summary>
    /// <response code="409">Запрос без пользователя.</response>
    [HttpPost]
    [ProducesResponseType(typeof(ImportDialogsResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ConflictError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Import(
        [FromBody] ImportDialogsCommand command,
        [FromServices] ImportDialogsCommandHandler handler,
        CancellationToken ct
    )
    {
        return FromResult((await handler.Handle(command, ct)).Value);
    }

    /// <summary>
    /// Сообщение обработано: пользователь ответил сам или решил не отвечать.
    /// </summary>
    /// <response code="404">Чат не найден.</response>
    [HttpPost("{chatId:long}/handled")]
    [ProducesResponseType(typeof(EmployerMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotFoundError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkHandled(
        long chatId,
        [FromServices] MarkDialogHandledCommandHandler handler,
        CancellationToken ct
    )
    {
        return FromResult((await handler.Handle(new MarkDialogHandledCommand(chatId), ct)).Value);
    }

    /// <summary>
    /// Итог автоответа на отказ от расширения: sent = false — написать нельзя, error — почему.
    /// </summary>
    /// <response code="404">Чат не найден.</response>
    /// <response code="409">Автоответ не ждёт отправки.</response>
    [HttpPost("{chatId:long}/auto-reply")]
    [ProducesResponseType(typeof(EmployerMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotFoundError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ConflictError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReportAutoReply(
        long chatId,
        [FromBody] AutoReplyRequest request,
        [FromServices] ReportAutoReplyCommandHandler handler,
        CancellationToken ct
    )
    {
        var command = new ReportAutoReplyCommand { ChatId = chatId, Sent = request.Sent, Error = request.Error };
        return FromResult((await handler.Handle(command, ct)).Value);
    }
}

public record AutoReplyRequest(bool Sent, [MaxLength(500)] string? Error);
