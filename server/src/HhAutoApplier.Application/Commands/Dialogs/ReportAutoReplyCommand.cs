using System.ComponentModel.DataAnnotations;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Commands.Dialogs;

// Итог автоответа на отказ от расширения: отправлен или нельзя (переписка закрыта, поле ввода не найдено)
public record ReportAutoReplyCommand
{
    public long ChatId { get; init; }

    public bool Sent { get; init; }

    [MaxLength(500)]
    public string? Error { get; init; }
}

// На .NET 11: public union ReportAutoReplyResult(EmployerMessageDto, NotFoundError, ConflictError);
[GenerateOneOf]
public partial class ReportAutoReplyResult : OneOfBase<EmployerMessageDto, NotFoundError, ConflictError>;
