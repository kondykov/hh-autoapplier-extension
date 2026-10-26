using System.ComponentModel.DataAnnotations;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Commands.Dialogs;

// Чаты откликов со страницы hh.ru от расширения (#26): последнее сообщение каждого чата и состояние отклика
public record ImportDialogsCommand
{
    [Required]
    [MaxLength(200)]
    public required IReadOnlyList<DialogChat> Chats { get; init; }
}

public record DialogChat
{
    public long ChatId { get; init; }

    [MaxLength(64)]
    public string? VacancyId { get; init; }

    [MaxLength(300)]
    public string Title { get; init; } = "";

    [MaxLength(300)]
    public string Company { get; init; } = "";

    // Состояние отклика на hh.ru (applicantState): DISCARD, INVITATION, INTERVIEW, RESPONSE…
    [MaxLength(32)]
    public string? State { get; init; }

    public long MessageId { get; init; }

    [MaxLength(10000)]
    public string Text { get; init; } = "";

    public DateTimeOffset At { get; init; }

    // Последнее сообщение — от работодателя (false — от пользователя)
    public bool FromEmployer { get; init; }
}

// На .NET 11: public union ImportDialogsResult(ImportDialogsResultDto, ConflictError);
[GenerateOneOf]
public partial class ImportDialogsResult : OneOfBase<ImportDialogsResultDto, ConflictError>;
