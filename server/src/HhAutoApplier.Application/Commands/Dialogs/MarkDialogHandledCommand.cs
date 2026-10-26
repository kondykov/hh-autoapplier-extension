using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Commands.Dialogs;

// Пользователь ответил работодателю сам или решил не отвечать: сообщение уходит из списка необработанных
public record MarkDialogHandledCommand(long ChatId);

// На .NET 11: public union MarkDialogHandledResult(EmployerMessageDto, NotFoundError);
[GenerateOneOf]
public partial class MarkDialogHandledResult : OneOfBase<EmployerMessageDto, NotFoundError>;
