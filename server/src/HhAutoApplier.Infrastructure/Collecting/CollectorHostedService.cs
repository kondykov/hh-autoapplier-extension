using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Commands.Collecting;
using HhAutoApplier.Application.Commands.Llm;
using HhAutoApplier.Application.Commands.Notifications;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Application.Notifications;
using HhAutoApplier.Domain.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HhAutoApplier.Infrastructure.Collecting;

// Расписание сбора: сразу при старте (если включено) и дальше раз в интервал. Сам сбор — CollectVacanciesCommandHandler,
// после него — LLM-оценка новых, ответы на анкеты и письма для очереди (если LLM настроена), затем уведомления о проверке
public class CollectorHostedService(
    IServiceScopeFactory scopes,
    IOptions<CollectorOptions> options,
    TimeProvider clock,
    ILogger<CollectorHostedService> logger
) : BackgroundService
{
    private const int MaxIntervalMinutes = 7 * 24 * 60;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var opt = options.Value;
        // Ошибка в конфиге не должна ронять приложение: Task.Delay не принимает паузы длиннее ~49 дней
        var interval = TimeSpan.FromMinutes(Math.Clamp(opt.IntervalMinutes, 1, MaxIntervalMinutes));
        if (!opt.CollectOnStartup)
            await Task.Delay(interval, stop);

        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var services = scope.ServiceProvider;
                var notifier = services.GetRequiredService<Notifier>();
                // Сбои — администраторам, не чаще раза в сутки на вид: сбор повторяется каждый час
                var day = $"{clock.GetUtcNow():yyyy-MM-dd}";

                var collect = services.GetRequiredService<CollectVacanciesCommandHandler>();
                if ((await collect.Handle(new CollectVacanciesCommand(), stop)).Value is SourceUnavailableError blocked)
                    await notifier.NotifyAdminsAsync(NotificationKind.SourceBlocked, $"source-blocked:{day}",
                        $"Сбор вакансий остановлен: {blocked.Message}. Следующая попытка — по расписанию.", stop);

                if (services.GetRequiredService<ILlmClient>().IsConfigured)
                {
                    var evaluate = services.GetRequiredService<EvaluatePendingVacanciesCommandHandler>();
                    var evaluated = await evaluate.Handle(new EvaluatePendingVacanciesCommand { Limit = 200 }, stop);

                    var answers = services.GetRequiredService<AnswerQuestionnairesCommandHandler>();
                    var answered = await answers.Handle(new AnswerQuestionnairesCommand { Limit = 200 }, stop);

                    var letters = services.GetRequiredService<WriteCoverLettersCommandHandler>();
                    var written = await letters.Handle(new WriteCoverLettersCommand { Limit = 200 }, stop);

                    if (new[] { evaluated.Value, answered.Value, written.Value }.Select(StoppedBy).FirstOrDefault(r => r is not null) is { } reason)
                        await notifier.NotifyAdminsAsync(NotificationKind.LlmUnavailable, $"llm:{day}",
                            $"{reason}. Оценка, анкеты и письма продолжатся при следующем сборе.", stop);
                }

                // После писем: в сообщении о проверке уже есть письмо
                await services.GetRequiredService<NotifyReviewsCommandHandler>().Handle(new NotifyReviewsCommand(), stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Сбор вакансий завершился ошибкой");
            }

            await Task.Delay(interval, stop);
        }
    }

    // Почему проход LLM остановился раньше (лимит, провайдер); null — прошёл всё
    private static string? StoppedBy(object? result) => result switch
    {
        LlmEvaluationResultDto r => r.StoppedBy,
        QuestionnaireAnswersResultDto r => r.StoppedBy,
        CoverLettersResultDto r => r.StoppedBy,
        LlmUnavailableError e => e.Message,
        _ => null,
    };
}
