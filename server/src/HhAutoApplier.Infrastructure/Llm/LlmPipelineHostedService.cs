using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Commands.Dialogs;
using HhAutoApplier.Application.Commands.Llm;
using HhAutoApplier.Application.Commands.Notifications;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Application.Notifications;
using HhAutoApplier.Domain.Notifications;
using HhAutoApplier.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HhAutoApplier.Infrastructure.Llm;

// LLM-конвейер раз в минуту: оценка новых, ответы на анкеты, письма для очереди, уведомления о проверке, разбор ответов
// работодателей. Очередь — строки в БД, ждущие LLM. Отдельно от часового сбора: выдача из расширения (POST /api/import)
// и чаты оцениваются через минуту, а не со следующим сбором (2026-10-27 импорт ждал оценки больше двух часов).
// Один проход за раз — двойной оценки одной вакансии нет
public class LlmPipelineHostedService(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<LlmPipelineHostedService> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    // За проход: больше — до следующей минуты (провайдер ограничивает частоту запросов)
    private const int Batch = 20;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await RunOnceAsync(scope.ServiceProvider, stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "LLM-конвейер завершился ошибкой");
            }

            await Task.Delay(Interval, stop);
        }
    }

    private async Task RunOnceAsync(IServiceProvider services, CancellationToken stop)
    {
        if (services.GetRequiredService<ILlmClient>().IsConfigured)
        {
            // Без работы — без вызовов: обработчики пишут сводку в лог, раз в минуту это был бы шум
            var db = services.GetRequiredService<IAppDbContext>();
            var results = new List<object?>();
            if (await db.JobApplications.AnyAsync(a => a.Status == VacancyStatus.Pending, stop))
                results.Add((await services.GetRequiredService<EvaluatePendingVacanciesCommandHandler>().Handle(new EvaluatePendingVacanciesCommand { Limit = Batch }, stop)).Value);
            if (await db.JobApplications.AnyAsync(a => a.Status == VacancyStatus.Questionnaire, stop))
                results.Add((await services.GetRequiredService<AnswerQuestionnairesCommandHandler>().Handle(new AnswerQuestionnairesCommand { Limit = Batch }, stop)).Value);
            if (await db.JobApplications.AnyAsync(a => (a.Status == VacancyStatus.Queued || a.Status == VacancyStatus.Review) && a.CoverLetter == null, stop))
                results.Add((await services.GetRequiredService<WriteCoverLettersCommandHandler>().Handle(new WriteCoverLettersCommand { Limit = Batch }, stop)).Value);
            if (await db.EmployerMessages.AnyAsync(m => m.Kind == null, stop))
                results.Add((await services.GetRequiredService<AnalyzeDialogsCommandHandler>().Handle(new AnalyzeDialogsCommand { Limit = Batch }, stop)).Value);

            if (results.Select(StoppedBy).FirstOrDefault(r => r is not null) is { } reason)
                await services.GetRequiredService<Notifier>().NotifyAdminsAsync(NotificationKind.LlmUnavailable, $"llm:{clock.GetUtcNow():yyyy-MM-dd}",
                    $"{reason}. Оценка, анкеты, письма и разбор ответов продолжатся позже.", stop);
        }

        // После писем: в сообщении о проверке уже есть письмо
        await services.GetRequiredService<NotifyReviewsCommandHandler>().Handle(new NotifyReviewsCommand(), stop);
    }

    // Почему проход LLM остановился раньше (лимит, провайдер); null — прошёл всё
    private static string? StoppedBy(object? result) => result switch
    {
        LlmEvaluationResultDto r => r.StoppedBy,
        QuestionnaireAnswersResultDto r => r.StoppedBy,
        CoverLettersResultDto r => r.StoppedBy,
        DialogsAnalysisResultDto r => r.StoppedBy,
        LlmUnavailableError e => e.Message,
        _ => null,
    };
}
