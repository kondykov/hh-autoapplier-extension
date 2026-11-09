using System.ComponentModel.DataAnnotations;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain.Dialogs;
using OneOf;

namespace HhAutoApplier.Application.Queries.Dialogs;

// Сообщения работодателей, новые сверху. Handled: false — необработанные, true — обработанные, null — все;
// администратор — сообщения пользователя UserId
public record GetDialogsQuery
{
    public DialogKind? Kind { get; init; }

    public bool? Handled { get; init; }

    [Range(1, 500)]
    public int Take { get; init; } = 100;

    public Guid? UserId { get; init; }
}

// На .NET 11: public union GetDialogsResult(List<EmployerMessageDto>, ForbiddenError);
[GenerateOneOf]
public partial class GetDialogsResult : OneOfBase<List<EmployerMessageDto>, ForbiddenError>;
