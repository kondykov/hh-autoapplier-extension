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
    IUserService users,
    ILogger<WriteCoverLettersCommandHandler> logger
) : ICommandHandler<WriteCoverLettersCommand, WriteCoverLettersResult>
{
    // Попыток на письмо за проход: первая и одна исправленная, если цифры не из фактов
    private const int MaxAttempts = 2;

    public async Task<WriteCoverLettersResult> Handle(WriteCoverLettersCommand command, CancellationToken ct = default)
    {
        if (!llm.IsConfigured)
            return new LlmUnavailableError("LLM не настроена: задайте LLM_API_KEY");

        // Письма, которые дважды подряд вышли с цифрами не из фактов, сутки не переписываются: конвейер идёт раз в минуту,
        // без паузы это цикл запросов к LLM. Без письма расширение возьмёт свой шаблон
        var since = clock.GetUtcNow().AddDays(-1);
        var givenUp = await db.LlmCalls.Where(c => c.Purpose == CoverLetterWriting.Purpose && c.Error != null
                                                   && c.Error.StartsWith(CoverLetterWriting.FiguresError) && c.CreatedAt > since)
            .GroupBy(c => c.VacancyId).Where(g => g.Count() >= MaxAttempts).Select(g => g.Key).ToListAsync(ct);
        var queued = await db.ActiveApplications(await users.GetBlockedIdsAsync(ct))
            .Where(a => (a.Status == VacancyStatus.Queued || a.Status == VacancyStatus.Review) && a.CoverLetter == null && !givenUp.Contains(a.VacancyId))
            .OrderByDescending(a => a.Score).ThenByDescending(a => a.Vacancy.PublishedAt).Take(command.Limit).ToListAsync(ct);
        if (queued.Count == 0)
            return new CoverLettersResultDto(0, 0, 0, null);

        // Резюме ищется у владельца отклика: имена у разных пользователей совпадают
        var resumes = await db.Resumes.AsNoTracking().ToDictionaryAsync(r => (r.UserId, r.Name), ct);
        // Профиль кандидата (#35) — владельца отклика; пользователей немного, все профили — одним запросом
        var profiles = await db.CandidateProfiles.AsNoTracking().ToListAsync(ct);

        int written = 0, failed = 0;
        string? stoppedBy = null;
        foreach (var application in queued)
        {
            var resume = application.ResumeProfile is { } name ? resumes.GetValueOrDefault((application.UserId, name)) : null;
            var first = CoverLetterWriting.BuildRequest(application.Vacancy, resume, profiles.FirstOrDefault(p => p.UserId == application.UserId));
            var request = first;
            var done = false;
            for (var attempt = 1; attempt <= MaxAttempts && !done; attempt++)
            {
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

                // Не письмо — вакансия остаётся без письма: расширение возьмёт свой шаблон, если откликнется раньше
                if (CoverLetterWriting.Parse(result.Content) is not { } letter)
                {
                    db.LlmCalls.Add(LlmCall.Succeeded(CoverLetterWriting.Purpose, application.VacancyId, result.Model, prompt, result.Content,
                        result.PromptTokens, result.CompletionTokens, clock.GetUtcNow()));
                    break;
                }

                // Цифры сверяются с фактами первого запроса: повторный содержит само неверное письмо
                var figures = CoverLetterWriting.UnsupportedFigures(letter, first);
                if (figures.Count > 0)
                {
                    db.LlmCalls.Add(LlmCall.Rejected(CoverLetterWriting.Purpose, application.VacancyId, result.Model, prompt, result.Content,
                        $"{CoverLetterWriting.FiguresError}: {string.Join(", ", figures)}", result.PromptTokens, result.CompletionTokens, clock.GetUtcNow()));
                    request = CoverLetterWriting.Retry(first, letter, figures);
                    continue;
                }

                db.LlmCalls.Add(LlmCall.Succeeded(CoverLetterWriting.Purpose, application.VacancyId, result.Model, prompt, result.Content,
                    result.PromptTokens, result.CompletionTokens, clock.GetUtcNow()));
                application.SetCoverLetter(letter, result.Model, clock.GetUtcNow());
                done = true;
            }

            await db.SaveChangesAsync(ct);
            if (stoppedBy is not null)
                break;
            if (done)
                written++;
            else
                failed++;
        }

        var remaining = await db.JobApplications.CountAsync(a => (a.Status == VacancyStatus.Queued || a.Status == VacancyStatus.Review) && a.CoverLetter == null, ct);
        logger.LogInformation("Письма LLM: написано {Written}, не разобрано {Failed}, без письма в очереди {Remaining}",
            written, failed, remaining);
        return new CoverLettersResultDto(written, failed, remaining, stoppedBy);
    }
}
