using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Commands.Collecting;
using HhAutoApplier.Application.Commands.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HhAutoApplier.Infrastructure.Collecting;

// Расписание сбора: сразу при старте (если включено) и дальше раз в интервал. Сам сбор — CollectVacanciesCommandHandler,
// после него — LLM-оценка новых, ответы на анкеты и письма для очереди (если LLM настроена)
public class CollectorHostedService(
    IServiceScopeFactory scopes,
    IOptions<CollectorOptions> options,
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
                var collect = scope.ServiceProvider.GetRequiredService<CollectVacanciesCommandHandler>();
                await collect.Handle(new CollectVacanciesCommand(), stop);

                if (scope.ServiceProvider.GetRequiredService<ILlmClient>().IsConfigured)
                {
                    var evaluate = scope.ServiceProvider.GetRequiredService<EvaluatePendingVacanciesCommandHandler>();
                    await evaluate.Handle(new EvaluatePendingVacanciesCommand { Limit = 200 }, stop);

                    var answers = scope.ServiceProvider.GetRequiredService<AnswerQuestionnairesCommandHandler>();
                    await answers.Handle(new AnswerQuestionnairesCommand { Limit = 200 }, stop);

                    var letters = scope.ServiceProvider.GetRequiredService<WriteCoverLettersCommandHandler>();
                    await letters.Handle(new WriteCoverLettersCommand { Limit = 200 }, stop);
                }
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
}
