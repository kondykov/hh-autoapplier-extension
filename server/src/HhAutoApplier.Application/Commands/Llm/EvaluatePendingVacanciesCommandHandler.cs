using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Application.Llm;
using HhAutoApplier.Domain.Llm;
using HhAutoApplier.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HhAutoApplier.Application.Commands.Llm;

public class EvaluatePendingVacanciesCommandHandler(
    IAppDbContext db,
    ILlmClient llm,
    TimeProvider clock,
    ILogger<EvaluatePendingVacanciesCommandHandler> logger
) : ICommandHandler<EvaluatePendingVacanciesCommand, EvaluatePendingResult>
{
    public async Task<EvaluatePendingResult> Handle(EvaluatePendingVacanciesCommand command, CancellationToken ct = default)
    {
        if (!llm.IsConfigured)
            return new LlmUnavailableError("LLM не настроена: задайте LLM_API_KEY");

        var pending = await db.Applications().Where(a => a.Status == VacancyStatus.Pending)
            .OrderBy(a => a.Vacancy.CollectedAt).Take(command.Limit).ToListAsync(ct);
        var resumes = await db.Resumes.AsNoTracking().ToListAsync(ct);
        var searches = await db.Searches.AsNoTracking().ToDictionaryAsync(s => s.Name, ct);

        int fit = 0, doubtful = 0, rejected = 0;
        string? stoppedBy = null;
        foreach (var application in pending)
        {
            var rules = searches.TryGetValue(application.Vacancy.SearchName, out var search) ? search.Rules : new();
            var request = VacancyAssessment.BuildRequest(application.Vacancy, resumes, rules);
            var prompt = VacancyAssessment.Serialize(request);

            LlmResult result;
            try
            {
                result = await llm.CompleteAsync(request, ct);
            }
            catch (LlmUnavailableException e)
            {
                // Лимит или провайдер недоступен: остальные остаются Pending до следующего запуска
                db.LlmCalls.Add(LlmCall.Failed(VacancyAssessment.Purpose, application.VacancyId, "", prompt, e.Message, clock.GetUtcNow()));
                await db.SaveChangesAsync(ct);
                stoppedBy = e.Message;
                break;
            }

            db.LlmCalls.Add(LlmCall.Succeeded(VacancyAssessment.Purpose, application.VacancyId, result.Model, prompt, result.Content,
                result.PromptTokens, result.CompletionTokens, clock.GetUtcNow()));

            // Неразобранный ответ — на ручную проверку, чтобы вакансия не застряла в Pending
            var assessment = VacancyAssessment.Parse(result.Content, resumes)
                             ?? new LlmAssessment(LlmVerdict.Doubtful, application.Score, null, "ответ LLM не разобран — проверить вручную");
            application.ApplyLlmEvaluation(assessment.Verdict, assessment.Score, assessment.Reason, assessment.Resume, result.Model, clock.GetUtcNow(),
                reviewDoubtful: !rules.SkipReview);
            await db.SaveChangesAsync(ct);

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

        var remaining = await db.Applications().CountAsync(a => a.Status == VacancyStatus.Pending, ct);
        logger.LogInformation("LLM-оценка: подходит {Fit}, сомнительно {Doubtful}, не подходит {Rejected}, ждут {Remaining}",
            fit, doubtful, rejected, remaining);
        return new LlmEvaluationResultDto(fit + doubtful + rejected, fit, doubtful, rejected, remaining, stoppedBy);
    }
}
