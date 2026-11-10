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
    LlmPipelineGate gate,
    LlmUserQuota quota,
    ILogger<AnswerQuestionnairesCommandHandler> logger
) : ICommandHandler<AnswerQuestionnairesCommand, AnswerQuestionnairesResult>
{
    // Первый ответ и одна исправленная попытка
    private const int MaxAttempts = 2;

    public async Task<AnswerQuestionnairesResult> Handle(AnswerQuestionnairesCommand command, CancellationToken ct = default)
    {
        if (!llm.IsConfigured)
            return new LlmUnavailableError("LLM не настроена: задайте LLM_API_KEY");

        using var pass = await gate.EnterAsync(ct);
        var blocked = await users.GetBlockedIdsAsync(ct);
        var usage = await quota.LoadAsync(ct);
        var exhausted = usage.Exhausted;
        var waiting = await db.ActiveApplications(blocked).Where(a => a.Status == VacancyStatus.Questionnaire && !exhausted.Contains(a.UserId))
            .OrderByDescending(a => a.Score).ThenByDescending(a => a.Vacancy.PublishedAt).Take(command.Limit).ToListAsync(ct);
        // Резюме ищется у владельца отклика: имена у разных пользователей совпадают
        var resumes = await db.ResumesOf(waiting).ToDictionaryAsync(r => (r.UserId, r.Name), ct);
        // Профиль кандидата (#35) — владельца отклика
        var profiles = await db.ProfilesOf(waiting).ToListAsync(ct);

        int answered = 0, failed = 0;
        string? stoppedBy = null;
        foreach (var application in waiting)
        {
            // Доля кончилась за этот проход: анкета ждёт следующих суток
            if (usage.IsExhausted(application.UserId))
                continue;

            var resume = application.ResumeProfile is { } name ? resumes.GetValueOrDefault((application.UserId, name)) : null;
            var rules = application.Rules;
            var profile = profiles.FirstOrDefault(p => p.UserId == application.UserId);
            var first = QuestionnaireAnswering.BuildRequest(application, resume, rules, profile);
            var facts = QuestionnaireAnswering.Facts(application, resume, profile);

            // Цифры текстовых ответов не из фактов — одна повторная попытка с замечанием, потом на ручную проверку (#58)
            var request = first;
            IReadOnlyList<IReadOnlyCollection<string>>? answers = null;
            IReadOnlyList<string> problems = [];
            string? incomplete = null;
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                // На повтор доли не хватило — ответы первой попытки с замечаниями идут на ручную проверку
                if (!usage.TryTake(application.UserId))
                    break;

                var prompt = VacancyAssessment.Serialize(request);
                LlmResult result;
                try
                {
                    result = await llm.CompleteAsync(request, ct);
                }
                catch (LlmUnavailableException e)
                {
                    // Лимит или провайдер недоступен: остальные ждут следующего запуска
                    db.LlmCalls.Add(LlmCall.Failed(QuestionnaireAnswering.Purpose, application.VacancyId, "", prompt, e.Message, clock.GetUtcNow()).For(application.UserId));
                    await db.SaveChangesAsync(ct);
                    stoppedBy = e.Message;
                    break;
                }

                answers = QuestionnaireAnswering.Parse(result.Content);
                incomplete = answers is null ? null : QuestionnaireAnswering.Incomplete(application.Questions, answers);
                problems = answers is null || incomplete is not null ? [] : QuestionnaireAnswering.Problems(application.Questions, answers, facts, rules);
                var error = answers is null ? LlmCall.Unparsed
                    : incomplete is not null ? $"{QuestionnaireAnswering.IncompleteError}: {incomplete}"
                    : problems.Count > 0 ? $"{QuestionnaireAnswering.FiguresError}: {string.Join("; ", problems)}"
                    : null;
                db.LlmCalls.Add((error is not null
                    ? LlmCall.Rejected(QuestionnaireAnswering.Purpose, application.VacancyId, result.Model, prompt, result.Content,
                        error, result.PromptTokens, result.CompletionTokens, clock.GetUtcNow())
                    : LlmCall.Succeeded(QuestionnaireAnswering.Purpose, application.VacancyId, result.Model, prompt, result.Content,
                        result.PromptTokens, result.CompletionTokens, clock.GetUtcNow())).For(application.UserId));
                if (answers is null || (incomplete is null && problems.Count == 0))
                    break;

                request = incomplete is not null
                    ? QuestionnaireAnswering.RetryIncomplete(first, result.Content, incomplete, application.Questions.Count)
                    : QuestionnaireAnswering.Retry(first, result.Content, problems);
            }
            // Анкета остаётся ждать следующего запуска
            if (stoppedBy is not null)
                break;

            // Неполный ответ или вариант не из анкеты — догадки работодателю не отправляем: пропуск с причиной,
            // чтобы не спрашивать LLM каждый запуск. Вернуть в очередь — вручную, расширение пришлёт анкету заново
            var ok = true;
            // Ответов не столько, сколько вопросов, и после повтора: решает человек, а не пропуск
            if (incomplete is not null)
            {
                application.RequireQuestionnaireReview(incomplete, clock.GetUtcNow());
                ok = false;
            }
            else
            {
                try
                {
                    application.AnswerQuestions(answers ?? throw new DomainException("ответ LLM не разобран"), clock.GetUtcNow(),
                        reviewText: !rules.SkipReview, problems);
                }
                catch (DomainException e)
                {
                    application.Skip($"анкета без ответа LLM: {e.Message}", clock.GetUtcNow());
                    ok = false;
                }
            }

            if (!await db.SaveOrDiscardAsync(application, ct))
            {
                logger.LogInformation("Анкета LLM {Vacancy}: отклик изменился, пока LLM отвечала, — ответы отброшены", application.VacancyId);
                continue;
            }
            if (ok)
                answered++;
            else
                failed++;

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
