using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Domain.Dialogs;

namespace HhAutoApplier.Application.Extractors;

public class EmployerMessageExtractor : IExtractor<EmployerMessage, EmployerMessageDto>
{
    public EmployerMessageDto Extract(EmployerMessage source) => new()
    {
        ChatId = source.ChatId,
        VacancyId = source.VacancyId,
        Title = source.Title,
        Company = source.Company,
        Text = source.Text,
        At = source.At,
        HhState = source.HhState,
        Kind = source.Kind,
        Summary = source.Summary,
        SuggestedReply = source.SuggestedReply,
        LlmModel = source.LlmModel,
        HandledAt = source.HandledAt,
        AutoReply = source.AutoReply,
        AutoReplyError = source.AutoReplyError,
    };
}
