using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Application.Llm;
using HhAutoApplier.Domain;
using HhAutoApplier.Domain.Llm;
using HhAutoApplier.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HhAutoApplier.Application.Commands.Llm;

public class AnswerQuestionnairesCommandHandler(
    IAppDbContext db,
    ILlmClient llm,
    TimeProvider clock,
    ILogger<AnswerQuestionnairesCommandHandler> logger
) : ICommandHandler<AnswerQuestionnairesCommand, AnswerQuestionnairesResult>
{
    public async Task<AnswerQuestionnairesResult> Handle(AnswerQuestionnairesCommand command, CancellationToken ct = default)
    {
        if (!llm.IsConfigured)
            return new LlmUnavailableError("LLM не настроена: задайте LLM_API_KEY");

        var waiting = await db.Vacancies.Where(v => v.Status == VacancyStatus.Questionnaire)
            .OrderByDescending(v => v.Score).ThenByDescending(v => v.PublishedAt).Take(command.Limit).ToListAsync(ct);
        var resumes = await db.Resumes.AsNoTracking().ToDictionaryAsync(r => r.Name, ct);
        var searches = await db.Searches.AsNoTracking().ToDictionaryAsync(s => s.Name, ct);

        int answered = 0, failed = 0;
        string? stoppedBy = null;
        foreach (var vacancy in waiting)
        {
            var resume = vacancy.ResumeProfile is { } name ? resumes.GetValueOrDefault(name) : null;
            var rules = searches.TryGetValue(vacancy.SearchName, out var search) ? search.Rules : new();
            var request = QuestionnaireAnswering.BuildRequest(vacancy, resume, rules);
            var prompt = VacancyAssessment.Serialize(request);

            LlmResult result;
            try
            {
                result = await llm.CompleteAsync(request, ct);
            }
            catch (LlmUnavailableException e)
            {
                // Лимит или провайдер недоступен: остальные ждут следующего запуска
                db.LlmCalls.Add(LlmCall.Failed(QuestionnaireAnswering.Purpose, vacancy.Id, "", prompt, e.Message, clock.GetUtcNow()));
                await db.SaveChangesAsync(ct);
                stoppedBy = e.Message;
                break;
            }

            db.LlmCalls.Add(LlmCall.Succeeded(QuestionnaireAnswering.Purpose, vacancy.Id, result.Model, prompt, result.Content,
                result.PromptTokens, result.CompletionTokens, clock.GetUtcNow()));

            // Неполный ответ или вариант не из анкеты — догадки работодателю не отправляем: пропуск с причиной,
            // чтобы не спрашивать LLM каждый запуск. Вернуть в очередь — вручную, расширение пришлёт анкету заново
            try
            {
                vacancy.AnswerQuestions(QuestionnaireAnswering.Parse(result.Content) ?? throw new DomainException("ответ LLM не разобран"));
                answered++;
            }
            catch (DomainException e)
            {
                vacancy.Skip($"анкета без ответа LLM: {e.Message}");
                failed++;
            }

            await db.SaveChangesAsync(ct);
        }

        var remaining = await db.Vacancies.CountAsync(v => v.Status == VacancyStatus.Questionnaire, ct);
        logger.LogInformation("Анкеты LLM: с ответами {Answered}, не разобрано {Failed}, ждут {Remaining}", answered, failed, remaining);
        return new QuestionnaireAnswersResultDto(answered, failed, remaining, stoppedBy);
    }
}
