using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Application.Llm;
using HhAutoApplier.Domain.Applications;
using HhAutoApplier.Domain.Candidates;
using HhAutoApplier.Domain.Llm;
using HhAutoApplier.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HhAutoApplier.Application.Commands.Llm;

public class EvaluatePendingVacanciesCommandHandler(
    IAppDbContext db,
    ILlmClient llm,
    TimeProvider clock,
    IUserService users,
    LlmPipelineGate gate,
    LlmUserQuota quota,
    ILogger<EvaluatePendingVacanciesCommandHandler> logger
) : ICommandHandler<EvaluatePendingVacanciesCommand, EvaluatePendingResult>
{
    public async Task<EvaluatePendingResult> Handle(EvaluatePendingVacanciesCommand command, CancellationToken ct = default)
    {
        if (!llm.IsConfigured)
            return new LlmUnavailableError("LLM не настроена: задайте LLM_API_KEY");

        using var pass = await gate.EnterAsync(ct);
        var usage = await quota.LoadAsync(ct);
        var exhausted = usage.Exhausted;
        var pending = await db.ActiveApplications(await users.GetBlockedIdsAsync(ct)).Where(a => a.Status == VacancyStatus.Pending && !exhausted.Contains(a.UserId))
            .OrderBy(a => a.Vacancy.CollectedAt).Take(command.Limit).ToListAsync(ct);
        // Резюме — владельца отклика, правила — поиска, который нашёл вакансию
        var resumesByOwner = (await db.ResumesOf(pending).ToListAsync(ct)).ToLookup(r => r.UserId);
        // Профиль кандидата (#35) — владельца отклика
        var profiles = await db.ProfilesOf(pending).ToListAsync(ct);

        int fit = 0, doubtful = 0, rejected = 0;
        string? stoppedBy = null;
        foreach (var application in pending)
        {
            // Доля кончилась за этот проход: отклик ждёт следующих суток
            if (!usage.TryTake(application.UserId))
                continue;

            var rules = application.Rules;
            var resumes = resumesByOwner[application.UserId].ToList();
            var request = VacancyAssessment.BuildRequest(application.Vacancy, resumes, rules,
                profiles.FirstOrDefault(p => p.UserId == application.UserId));
            var prompt = VacancyAssessment.Serialize(request);

            LlmResult result;
            try
            {
                result = await llm.CompleteAsync(request, ct);
            }
            catch (LlmUnavailableException e)
            {
                // Лимит или провайдер недоступен: остальные остаются Pending до следующего запуска
                db.LlmCalls.Add(LlmCall.Failed(VacancyAssessment.Purpose, application.VacancyId, "", prompt, e.Message, clock.GetUtcNow()).For(application.UserId));
                await db.SaveChangesAsync(ct);
                stoppedBy = e.Message;
                break;
            }

            var parsed = VacancyAssessment.Parse(result.Content, resumes);
            db.LlmCalls.Add((parsed is null
                ? LlmCall.Rejected(VacancyAssessment.Purpose, application.VacancyId, result.Model, prompt, result.Content,
                    LlmCall.Unparsed, result.PromptTokens, result.CompletionTokens, clock.GetUtcNow())
                : LlmCall.Succeeded(VacancyAssessment.Purpose, application.VacancyId, result.Model, prompt, result.Content,
                    result.PromptTokens, result.CompletionTokens, clock.GetUtcNow())).For(application.UserId));

            // Неразобранный ответ — на ручную проверку, чтобы вакансия не застряла в Pending
            var assessment = parsed
                             ?? new LlmAssessment(LlmVerdict.Doubtful, application.Score, null, "ответ LLM не разобран — проверить вручную");

            // Просьбы к письму (#72) — до смены статуса, иначе расширение заберёт отклик раньше.
            // Доля кончилась или провайдер недоступен: просьбы неизвестны, отклик остаётся Pending и оценится заново
            if (assessment.Verdict != LlmVerdict.Reject)
            {
                var (ready, unavailable) = await ExtractLetterRequestsAsync(application, profiles.FirstOrDefault(p => p.UserId == application.UserId), usage, ct);
                if (!ready)
                {
                    await db.SaveChangesAsync(ct);
                    if (unavailable is null)
                        continue;

                    stoppedBy = unavailable;
                    break;
                }
            }

            application.ApplyLlmEvaluation(assessment.Verdict, assessment.Score, assessment.Reason, assessment.Resume, result.Model, clock.GetUtcNow(),
                reviewDoubtful: !rules.SkipReview);
            if (!await db.SaveOrDiscardAsync(application, ct))
            {
                logger.LogInformation("LLM-оценка {Vacancy}: отклик изменился, пока LLM отвечала, — результат отброшен", application.VacancyId);
                continue;
            }

            switch (assessment.Verdict)
            {
                case LlmVerdict.Fit:
                    fit++;
                    break;
                case LlmVerdict.Doubtful:
                    doubtful++;
                    break;
                default:
                    rejected++;
                    break;
            }
        }

        var remaining = await db.JobApplications.CountAsync(a => a.Status == VacancyStatus.Pending, ct);
        logger.LogInformation("LLM-оценка: подходит {Fit}, сомнительно {Doubtful}, не подходит {Rejected}, ждут {Remaining}",
            fit, doubtful, rejected, remaining);
        return new LlmEvaluationResultDto(fit + doubtful + rejected, fit, doubtful, rejected, remaining, stoppedBy);
    }

    // Ready — итог записан в отклик. Иначе отклик ждёт следующего прохода; Unavailable — причина остановить проход.
    // Неразобранный ответ — неизвестно, а не «просьб нет»: две попытки, затем сбой и ручная проверка. Запросов на отклик за сутки — не больше двух
    private async Task<(bool Ready, string? Unavailable)> ExtractLetterRequestsAsync(JobApplication application, CandidateProfile? profile,
        LlmQuotaUsage usage, CancellationToken ct)
    {
        if (!LetterRequestExtraction.MayContainRequests(application.Vacancy.Description))
        {
            application.RecordLetterRequests([], profile);
            return (true, null);
        }

        var since = clock.GetUtcNow().AddDays(-1);
        var used = await db.LlmCalls.CountAsync(c => c.Purpose == LetterRequestExtraction.Purpose && c.Model != "" && c.CreatedAt > since
                                                     && c.VacancyId == application.VacancyId && c.UserId == application.UserId, ct);
        var request = LetterRequestExtraction.BuildRequest(application.Vacancy);
        var prompt = VacancyAssessment.Serialize(request);
        for (var attempt = used; attempt < LetterRequestExtraction.MaxRequestsPerDay; attempt++)
        {
            if (!usage.TryTake(application.UserId))
                return (false, null);

            LlmResult result;
            try
            {
                result = await llm.CompleteAsync(request, ct);
            }
            catch (LlmUnavailableException e)
            {
                db.LlmCalls.Add(LlmCall.Failed(LetterRequestExtraction.Purpose, application.VacancyId, "", prompt, e.Message, clock.GetUtcNow()).For(application.UserId));
                return (false, e.Message);
            }

            var requests = LetterRequestExtraction.Parse(result.Content, application.Vacancy);
            db.LlmCalls.Add((requests is null
                ? LlmCall.Rejected(LetterRequestExtraction.Purpose, application.VacancyId, result.Model, prompt, result.Content,
                    LlmCall.Unparsed, result.PromptTokens, result.CompletionTokens, clock.GetUtcNow())
                : LlmCall.Succeeded(LetterRequestExtraction.Purpose, application.VacancyId, result.Model, prompt, result.Content,
                    result.PromptTokens, result.CompletionTokens, clock.GetUtcNow())).For(application.UserId));
            if (requests is not null)
            {
                application.RecordLetterRequests(requests, profile);
                return (true, null);
            }
        }

        application.MarkLetterRequestsFailed();
        return (true, null);
    }
}
