using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Application.Llm;
using HhAutoApplier.Application.Notifications;
using HhAutoApplier.Domain.Dialogs;
using HhAutoApplier.Domain.Llm;
using HhAutoApplier.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HhAutoApplier.Application.Commands.Dialogs;

public class AnalyzeDialogsCommandHandler(
    IAppDbContext db,
    ILlmClient llm,
    TimeProvider clock,
    IUserService users,
    Notifier notifier,
    ILogger<AnalyzeDialogsCommandHandler> logger
) : ICommandHandler<AnalyzeDialogsCommand, AnalyzeDialogsResult>
{
    public async Task<AnalyzeDialogsResult> Handle(AnalyzeDialogsCommand command, CancellationToken ct = default)
    {
        if (!llm.IsConfigured)
            return new LlmUnavailableError("LLM не настроена: задайте LLM_API_KEY");

        var blocked = await users.GetBlockedIdsAsync(ct);
        var waiting = await db.EmployerMessages.Where(m => m.Kind == null && !blocked.Contains(m.UserId))
            .OrderBy(m => m.ReceivedAt).Take(command.Limit).ToListAsync(ct);
        if (waiting.Count == 0)
            return new DialogsAnalysisResultDto(0, 0, 0, null);

        // Резюме — то, с которым откликались на вакансию чата
        var userIds = waiting.Select(m => m.UserId).Distinct().ToList();
        var vacancyIds = waiting.Select(m => m.VacancyId).OfType<string>().Distinct().ToList();
        var applied = await db.JobApplications.AsNoTracking()
            .Where(a => a.UserId != null && userIds.Contains(a.UserId.Value) && vacancyIds.Contains(a.VacancyId) && a.ResumeProfile != null)
            .Select(a => new { UserId = a.UserId!.Value, a.VacancyId, a.ResumeProfile })
            .ToDictionaryAsync(a => (a.UserId, a.VacancyId), a => a.ResumeProfile!, ct);
        var resumes = await db.Resumes.AsNoTracking().Where(r => r.UserId != null && userIds.Contains(r.UserId.Value))
            .ToDictionaryAsync(r => (r.UserId!.Value, r.Name), ct);

        int analyzed = 0, failed = 0;
        string? stoppedBy = null;
        foreach (var message in waiting)
        {
            var resume = message.VacancyId is { } v && applied.GetValueOrDefault((message.UserId, v)) is { } name
                ? resumes.GetValueOrDefault((message.UserId, name))
                : null;
            var request = DialogAnalysis.BuildRequest(message, resume);
            var prompt = VacancyAssessment.Serialize(request);
            var subject = message.VacancyId ?? $"chat:{message.ChatId}";

            LlmResult result;
            try
            {
                result = await llm.CompleteAsync(request, ct);
            }
            catch (LlmUnavailableException e)
            {
                // Лимит или провайдер недоступен: остальные ждут следующего прохода
                db.LlmCalls.Add(LlmCall.Failed(DialogAnalysis.Purpose, subject, "", prompt, e.Message, clock.GetUtcNow()));
                await db.SaveChangesAsync(ct);
                stoppedBy = e.Message;
                break;
            }

            db.LlmCalls.Add(LlmCall.Succeeded(DialogAnalysis.Purpose, subject, result.Model, prompt, result.Content,
                result.PromptTokens, result.CompletionTokens, clock.GetUtcNow()));

            // Не разобрано — «прочее» без черновика: иначе сообщение разбиралось бы каждый проход и тратило лимит
            if (DialogAnalysis.Parse(result.Content) is { } analysis)
            {
                message.ApplyAnalysis(analysis.Kind, analysis.Summary, analysis.Reply, result.Model, clock.GetUtcNow());
                analyzed++;
            }
            else
            {
                message.ApplyAnalysis(DialogKind.Other, "LLM не разобрала сообщение", null, result.Model, clock.GetUtcNow());
                failed++;
            }

            await db.SaveChangesAsync(ct);

            if (message.Kind is DialogKind.Invitation or DialogKind.Question)
                await notifier.NotifyAsync(message.UserId, NotificationKind.EmployerReply, $"dialog:{message.ChatId}:{message.MessageId}",
                    BotTexts.EmployerReply(message), message.VacancyId, ct);
        }

        var remaining = await db.EmployerMessages.CountAsync(m => m.Kind == null, ct);
        logger.LogInformation("Разбор ответов работодателей: разобрано {Analyzed}, не разобрано {Failed}, ждут {Remaining}",
            analyzed, failed, remaining);
        return new DialogsAnalysisResultDto(analyzed, failed, remaining, stoppedBy);
    }
}
