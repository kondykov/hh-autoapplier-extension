using System.Diagnostics;
using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;

namespace HhAutoApplier.Application.Commands.Llm;

public class CheckLlmCommandHandler(ILlmClient llm) : ICommandHandler<CheckLlmCommand, CheckLlmResult>
{
    public async Task<CheckLlmResult> Handle(CheckLlmCommand command, CancellationToken ct = default)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            var result = await llm.CompleteAsync(
                new LlmRequest([LlmMessage.User("Ответь одним словом: ok")], Temperature: 0, MaxTokens: 200),
                ct);
            return new LlmCheckDto(result.Model, result.Content.Trim(), (int)timer.ElapsedMilliseconds, result.PromptTokens + result.CompletionTokens);
        }
        catch (LlmUnavailableException e)
        {
            return new LlmUnavailableError(e.Message);
        }
    }
}
