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

public class WriteCoverLettersCommandHandler(
    IAppDbContext db,
    ILlmClient llm,
    TimeProvider clock,
    ILogger<WriteCoverLettersCommandHandler> logger
) : ICommandHandler<WriteCoverLettersCommand, WriteCoverLettersResult>
{
    public async Task<WriteCoverLettersResult> Handle(WriteCoverLettersCommand command, CancellationToken ct = default)
    {
        if (!llm.IsConfigured)
            return new LlmUnavailableError("LLM не настроена: задайте LLM_API_KEY");

        var queued = await db.Applications().Where(a => (a.Status == VacancyStatus.Queued || a.Status == VacancyStatus.Review) && a.CoverLetter == null)
            .OrderByDescending(a => a.Score).ThenByDescending(a => a.Vacancy.PublishedAt).Take(command.Limit).ToListAsync(ct);
        var resumes = await db.Resumes.AsNoTracking().ToDictionaryAsync(r => r.Name, ct);

        int written = 0, failed = 0;
        string? stoppedBy = null;
        foreach (var application in queued)
        {
            var resume = application.ResumeProfile is { } name ? resumes.GetValueOrDefault(name) : null;
            var request = CoverLetterWriting.BuildRequest(application.Vacancy, resume);
            var prompt = VacancyAssessment.Serialize(request);

            LlmResult result;
            try
            {
                result = await llm.CompleteAsync(request, ct);
            }
            catch (LlmUnavailableException e)
            {
                // Лимит или провайдер недоступен: остальные остаются без письма до следующего запуска
                db.LlmCalls.Add(LlmCall.Failed(CoverLetterWriting.Purpose, application.VacancyId, "", prompt, e.Message, clock.GetUtcNow()));
                await db.SaveChangesAsync(ct);
                stoppedBy = e.Message;
                break;
            }

            db.LlmCalls.Add(LlmCall.Succeeded(CoverLetterWriting.Purpose, application.VacancyId, result.Model, prompt, result.Content,
                result.PromptTokens, result.CompletionTokens, clock.GetUtcNow()));

            // Не письмо — вакансия остаётся без письма: расширение возьмёт свой шаблон, если откликнется раньше
            if (CoverLetterWriting.Parse(result.Content) is { } letter)
            {
                application.SetCoverLetter(letter, result.Model, clock.GetUtcNow());
                written++;
            }
            else
            {
                failed++;
            }

            await db.SaveChangesAsync(ct);
        }

        var remaining = await db.Applications().CountAsync(a => (a.Status == VacancyStatus.Queued || a.Status == VacancyStatus.Review) && a.CoverLetter == null, ct);
        logger.LogInformation("Письма LLM: написано {Written}, не разобрано {Failed}, без письма в очереди {Remaining}",
            written, failed, remaining);
        return new CoverLettersResultDto(written, failed, remaining, stoppedBy);
    }
}
