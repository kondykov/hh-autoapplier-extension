using System.Diagnostics;
using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain.Llm;

namespace HhAutoApplier.Application.Commands.Llm;

public class CheckLlmCommandHandler(ILlmClient llm, IAppDbContext db, TimeProvider clock) : ICommandHandler<CheckLlmCommand, CheckLlmResult>
{
    private const string Purpose = "check";

    public async Task<CheckLlmResult> Handle(CheckLlmCommand command, CancellationToken ct = default)
    {
        var request = new LlmRequest([LlmMessage.User("Ответь одним словом: ok")], Temperature: 0, MaxTokens: 200);
        var prompt = request.Messages[0].Content;
        var timer = Stopwatch.StartNew();
        try
        {
            var result = await llm.CompleteAsync(request, ct);
            db.LlmCalls.Add(LlmCall.Succeeded(Purpose, null, result.Model, prompt, result.Content, result.PromptTokens, result.CompletionTokens, clock.GetUtcNow()));
            await db.SaveChangesAsync(ct);
            return new LlmCheckDto(result.Model, result.Content.Trim(), (int)timer.ElapsedMilliseconds, result.PromptTokens + result.CompletionTokens);
        }
        catch (LlmUnavailableException e)
        {
            if (llm.IsConfigured)
            {
                db.LlmCalls.Add(LlmCall.Failed(Purpose, null, "", prompt, e.Message, clock.GetUtcNow()));
                await db.SaveChangesAsync(ct);
            }
            return new LlmUnavailableError(e.Message);
        }
    }
}
