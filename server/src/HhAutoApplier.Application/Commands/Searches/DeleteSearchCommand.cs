using HhAutoApplier.Application.Errors;
using OneOf;
using OneOf.Types;

namespace HhAutoApplier.Application.Commands.Searches;

public record DeleteSearchCommand(int Id);

// На .NET 11: public union DeleteSearchResult(Success, NotFoundError);
[GenerateOneOf]
public partial class DeleteSearchResult : OneOfBase<Success, NotFoundError>;
