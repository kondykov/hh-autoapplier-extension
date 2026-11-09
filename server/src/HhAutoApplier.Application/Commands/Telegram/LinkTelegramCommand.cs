using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Commands.Telegram;

// Код привязки чата Telegram: только после входа по паролю — чат получает право одобрять и отклонять отклики
public record LinkTelegramCommand;

// На .NET 11: public union LinkTelegramResult(TelegramLinkDto, ConflictError);
[GenerateOneOf]
public partial class LinkTelegramResult : OneOfBase<TelegramLinkDto, ConflictError>;
