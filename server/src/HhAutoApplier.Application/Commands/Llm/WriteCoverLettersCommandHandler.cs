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
    LlmPipelineGate gate,
    LlmUserQuota quota,
    ILogger<WriteCoverLettersCommandHandler> logger
) : ICommandHandler<WriteCoverLettersCommand, WriteCoverLettersResult>
{
    // Попыток на письмо за проход: первая и одна исправленная, если письмо не прошло проверку
    private const int MaxAttempts = 2;

    public async Task<WriteCoverLettersResult> Handle(WriteCoverLettersCommand command, CancellationToken ct = default)
    {
        if (!llm.IsConfigured)
            return new LlmUnavailableError("LLM не настроена: задайте LLM_API_KEY");

        using var pass = await gate.EnterAsync(ct);

        // Письма, которые дважды не прошли сверку с фактами (цифры, заглушки, обещания, технологии) или не разобраны (#80), сутки не переписываются:
        // конвейер идёт раз в минуту, без паузы это цикл запросов к LLM. Сбой провайдера не считается: ответа не было, письма
        // должны написаться после восстановления. Без письма расширение возьмёт свой шаблон. Считается по отклику — вакансия
        // и её владелец (#57): неудачи с профилем одного пользователя не лишают писем другого
        var since = clock.GetUtcNow().AddDays(-1);
        var usage = await quota.LoadAsync(ct);
        var exhausted = usage.Exhausted;
        var queued = await db.ActiveApplications(await users.GetBlockedIdsAsync(ct))
            .Where(a => (a.Status == VacancyStatus.Queued || a.Status == VacancyStatus.Review) && a.CoverLetter == null && !exhausted.Contains(a.UserId))
            .Where(a => db.LlmCalls.Count(c => c.Purpose == CoverLetterWriting.Purpose && c.Error != null
                                               && (c.Error == LlmCall.Unparsed || c.Error.StartsWith(CoverLetterWriting.FiguresError) || c.Error.StartsWith(CoverLetterWriting.PlaceholdersError)
                                                   || c.Error.StartsWith(CoverLetterWriting.PromisesError) || c.Error.StartsWith(CoverLetterWriting.TechnologiesError))
                                               && c.CreatedAt > since && c.VacancyId == a.VacancyId && c.UserId == a.UserId) < MaxAttempts)
            .OrderByDescending(a => a.Score).ThenByDescending(a => a.Vacancy.PublishedAt).Take(command.Limit).ToListAsync(ct);
        if (queued.Count == 0)
            return new CoverLettersResultDto(0, 0, 0, null);

        // Резюме ищется у владельца отклика: имена у разных пользователей совпадают
        var resumes = await db.ResumesOf(queued).ToDictionaryAsync(r => (r.UserId, r.Name), ct);
        // Профиль кандидата (#35) — владельца отклика
        var profiles = await db.ProfilesOf(queued).ToListAsync(ct);

        int written = 0, failed = 0;
        string? stoppedBy = null;
        foreach (var application in queued)
        {
            // Доля кончилась за этот проход: письмо будет в следующие сутки, расширение возьмёт шаблон, если откликнется раньше
            if (usage.IsExhausted(application.UserId))
                continue;

            var resume = application.ResumeProfile is { } name ? resumes.GetValueOrDefault((application.UserId, name)) : null;
            var profile = profiles.FirstOrDefault(p => p.UserId == application.UserId);
            var first = CoverLetterWriting.BuildRequest(application.Vacancy, resume, profile);
            var facts = CoverLetterWriting.Facts(application.Vacancy, resume, profile);
            var request = first;
            var done = false;
            string? rejected = null;
            for (var attempt = 1; attempt <= MaxAttempts && !done; attempt++)
            {
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
                    // Лимит или провайдер недоступен: остальные остаются без письма до следующего запуска
                    db.LlmCalls.Add(LlmCall.Failed(CoverLetterWriting.Purpose, application.VacancyId, "", prompt, e.Message, clock.GetUtcNow()).For(application.UserId));
                    await db.SaveChangesAsync(ct);
                    stoppedBy = e.Message;
                    break;
                }

                // Не письмо — вакансия остаётся без письма: расширение возьмёт свой шаблон, если откликнется раньше
                if (CoverLetterWriting.Parse(result.Content) is not { } letter)
                {
                    db.LlmCalls.Add(LlmCall.Rejected(CoverLetterWriting.Purpose, application.VacancyId, result.Model, prompt, result.Content,
                        LlmCall.Unparsed, result.PromptTokens, result.CompletionTokens, clock.GetUtcNow()).For(application.UserId));
                    // С открытыми просьбами шаблон расширения не подходит: письмо пишет человек, иначе отклик завис бы вне очереди
                    if (application.AwaitsClosingLetter)
                        rejected = LlmCall.Unparsed;
                    break;
                }

                // Письмо сверяется только с фактами о кандидате: ни текст вакансии, ни неверное письмо в повторном запросе фактами не считаются
                var figures = CoverLetterWriting.UnsupportedFigures(letter, facts, application.Vacancy);
                var placeholders = CoverLetterWriting.Placeholders(letter, facts);
                var promises = CoverLetterClaims.Promises(letter, facts);
                var technologies = CoverLetterClaims.Technologies(letter, facts, application.Vacancy);
                if (figures.Count > 0 || placeholders.Count > 0 || promises.Count > 0 || technologies.Count > 0)
                {
                    rejected = CoverLetterWriting.Problem(figures, placeholders, promises, technologies);
                    db.LlmCalls.Add(LlmCall.Rejected(CoverLetterWriting.Purpose, application.VacancyId, result.Model, prompt, result.Content,
                        rejected, result.PromptTokens, result.CompletionTokens, clock.GetUtcNow()).For(application.UserId));
                    request = CoverLetterWriting.Retry(first, letter, figures, placeholders, promises, technologies);
                    continue;
                }

                db.LlmCalls.Add(LlmCall.Succeeded(CoverLetterWriting.Purpose, application.VacancyId, result.Model, prompt, result.Content,
                    result.PromptTokens, result.CompletionTokens, clock.GetUtcNow()).For(application.UserId));
                application.SetCoverLetter(letter, result.Model, clock.GetUtcNow());
                // Просьбы к письму (#72): проверки выше видели письмо модели, значения дописывает сервер
                application.CloseLetterRequests(profile, resume?.LetterTemplate, clock.GetUtcNow());
                done = true;
            }

            // Письмо не прошло проверку и после повтора: решает человек, черновик — в журнале LlmCalls. Письмо с заглушками
            // не записывается: «Одобрить» в Telegram отправило бы его как есть, без письма расширение возьмёт свой шаблон
            if (!done && rejected is not null && stoppedBy is null)
                application.RequireLetterReview(rejected, clock.GetUtcNow());

            // Пока LLM писала, отклик одобрили со своим письмом или отправили — письмо LLM не записывается поверх
            if (!await db.SaveOrDiscardAsync(application, ct))
            {
                logger.LogInformation("Письмо LLM {Vacancy}: отклик изменился, пока LLM писала, — письмо отброшено", application.VacancyId);
                done = false;
            }
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
