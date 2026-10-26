using HhAutoApplier.Application.Errors;
using OneOf;
using OneOf.Types;

namespace HhAutoApplier.Application.Commands.Telegram;

// Отвязать свой чат: бот перестаёт слать уведомления и принимать команды
public record UnlinkTelegramCommand;

// На .NET 11: public union UnlinkTelegramResult(Success, NotFoundError);
[GenerateOneOf]
public partial class UnlinkTelegramResult : OneOfBase<Success, NotFoundError>;
