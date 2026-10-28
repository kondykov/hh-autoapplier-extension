using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Application.Llm;
using HhAutoApplier.Application.Notifications;
using HhAutoApplier.Domain;
using HhAutoApplier.Domain.Llm;
using HhAutoApplier.Domain.Notifications;
using HhAutoApplier.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HhAutoApplier.Application.Commands.Llm;

public class AnswerQuestionnairesCommandHandler(
    IAppDbContext db,
    ILlmClient llm,
    TimeProvider clock,
    IUserService users,
    Notifier notifier,
    ILogger<AnswerQuestionnairesCommandHandler> logger
) : ICommandHandler<AnswerQuestionnairesCommand, AnswerQuestionnairesResult>
{
    public async Task<AnswerQuestionnairesResult> Handle(AnswerQuestionnairesCommand command, CancellationToken ct = default)
    {
        if (!llm.IsConfigured)
            return new LlmUnavailableError("LLM не настроена: задайте LLM_API_KEY");

        var blocked = await users.GetBlockedIdsAsync(ct);
        var waiting = await db.ActiveApplications(blocked).Where(a => a.Status == VacancyStatus.Questionnaire)
            .OrderByDescending(a => a.Score).ThenByDescending(a => a.Vacancy.PublishedAt).Take(command.Limit).ToListAsync(ct);
        // Резюме ищется у владельца отклика: имена у разных пользователей совпадают
        var resumes = await db.Resumes.AsNoTracking().ToDictionaryAsync(r => (r.UserId, r.Name), ct);
        // Профиль кандидата (#35) — владельца отклика; пользователей немного, все профили — одним запросом
        var profiles = await db.CandidateProfiles.AsNoTracking().ToListAsync(ct);

        int answered = 0, failed = 0;
        string? stoppedBy = null;
        foreach (var application in waiting)
        {
            var resume = application.ResumeProfile is { } name ? resumes.GetValueOrDefault((application.UserId, name)) : null;
            var rules = application.Rules;
            var request = QuestionnaireAnswering.BuildRequest(application, resume, rules, profiles.FirstOrDefault(p => p.UserId == application.UserId));
            var prompt = VacancyAssessment.Serialize(request);

            LlmResult result;
            try
            {
                result = await llm.CompleteAsync(request, ct);
            }
            catch (LlmUnavailableException e)
            {
                // Лимит или провайдер недоступен: остальные ждут следующего запуска
                db.LlmCalls.Add(LlmCall.Failed(QuestionnaireAnswering.Purpose, application.VacancyId, "", prompt, e.Message, clock.GetUtcNow()));
                await db.SaveChangesAsync(ct);
                stoppedBy = e.Message;
                break;
            }

            db.LlmCalls.Add(LlmCall.Succeeded(QuestionnaireAnswering.Purpose, application.VacancyId, result.Model, prompt, result.Content,
                result.PromptTokens, result.CompletionTokens, clock.GetUtcNow()));

            // Неполный ответ или вариант не из анкеты — догадки работодателю не отправляем: пропуск с причиной,
            // чтобы не спрашивать LLM каждый запуск. Вернуть в очередь — вручную, расширение пришлёт анкету заново
            try
            {
                application.AnswerQuestions(QuestionnaireAnswering.Parse(result.Content) ?? throw new DomainException("ответ LLM не разобран"), clock.GetUtcNow());
                answered++;
            }
            catch (DomainException e)
            {
                application.Skip($"анкета без ответа LLM: {e.Message}", clock.GetUtcNow());
                failed++;
            }

            await db.SaveChangesAsync(ct);

            if (application.Status == VacancyStatus.Skipped)
                await notifier.NotifyAsync(application.UserId, NotificationKind.Questionnaire, $"questionnaire:{application.Id}",
                    $"Анкета без ответа LLM, отклик пропущен: {application.Vacancy.Title}\n{application.Vacancy.Url}\n{application.StatusReason}\n" +
                    "Откликнуться можно вручную или вернуть в очередь — расширение пришлёт анкету заново.",
                    application.VacancyId, ct);
        }

        // Анкеты заблокированных не считаются: проход их не обрабатывает
        var remaining = await db.ActiveApplications(blocked).CountAsync(a => a.Status == VacancyStatus.Questionnaire, ct);
        logger.LogInformation("Анкеты LLM: с ответами {Answered}, не разобрано {Failed}, ждут {Remaining}", answered, failed, remaining);
        return new QuestionnaireAnswersResultDto(answered, failed, remaining, stoppedBy);
    }
}
