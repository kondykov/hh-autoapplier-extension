using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Commands.Dialogs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Application.Notifications;
using HhAutoApplier.Domain.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HhAutoApplier.Infrastructure.Dialogs;

// Разбор ответов работодателей (#26): очередь — неразобранные строки EmployerMessages. Расширение присылает чаты
// по кнопке — проход раз в минуту, чтобы приглашение пришло в Telegram вскоре после нажатия, а не со следующим сбором
public class DialogAnalysisHostedService(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<DialogAnalysisHostedService> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var services = scope.ServiceProvider;
                if (services.GetRequiredService<ILlmClient>().IsConfigured)
                {
                    var result = await services.GetRequiredService<AnalyzeDialogsCommandHandler>().Handle(new AnalyzeDialogsCommand { Limit = 20 }, stop);
                    if (result.Value is DialogsAnalysisResultDto { StoppedBy: { } reason })
                        await services.GetRequiredService<Notifier>().NotifyAdminsAsync(NotificationKind.LlmUnavailable, $"llm:{clock.GetUtcNow():yyyy-MM-dd}",
                            $"{reason}. Разбор ответов работодателей продолжится позже.", stop);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Разбор ответов работодателей завершился ошибкой");
            }

            await Task.Delay(Interval, stop);
        }
    }
}
