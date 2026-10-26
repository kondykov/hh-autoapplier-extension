using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Notifications;
using HhAutoApplier.Domain.Notifications;

namespace HhAutoApplier.Application.Commands.Notifications;

// Notified = false — некому доставить: бот не настроен, чат не привязан или такое сообщение уже было
public class ReportEventCommandHandler(ICurrentUser current, Notifier notifier, TimeProvider clock) : ICommandHandler<ReportEventCommand, ReportEventResultDto>
{
    public async Task<ReportEventResultDto> Handle(ReportEventCommand command, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var details = string.IsNullOrWhiteSpace(command.Message) ? "" : $"\n{command.Message.Trim()}";
        var (kind, key, text) = command.Kind switch
        {
            ExtensionEvent.Captcha => (NotificationKind.Captcha, $"captcha:{now:yyyy-MM-ddTHH}",
                $"Капча на hh.ru: расширение на паузе. Решите капчу в браузере и снимите паузу в панели.{details}"),
            _ => (NotificationKind.DailyLimit, $"daily-limit:{now:yyyy-MM-dd}",
                $"Дневной лимит откликов исчерпан: расширение продолжит завтра.{details}"),
        };
        return new ReportEventResultDto(await notifier.NotifyAsync(current.Id, kind, key, text, null, ct));
    }
}
