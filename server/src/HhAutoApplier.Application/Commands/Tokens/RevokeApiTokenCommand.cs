using HhAutoApplier.Application.Errors;
using OneOf;
using OneOf.Types;

namespace HhAutoApplier.Application.Commands.Tokens;

// Отозвать свой токен: расширение с ним сразу получит 401
public record RevokeApiTokenCommand(Guid Id);

// На .NET 11: public union RevokeApiTokenResult(Success, NotFoundError, ConflictError);
[GenerateOneOf]
public partial class RevokeApiTokenResult : OneOfBase<Success, NotFoundError, ConflictError>;