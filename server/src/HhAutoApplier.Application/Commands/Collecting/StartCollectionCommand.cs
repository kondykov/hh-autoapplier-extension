using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Commands.Collecting;

// Запустить сбор в фоне: проход не зависит от запроса и страницы, которая его начала (#74)
public record StartCollectionCommand;

// На .NET 11: public union StartCollectionResult(CollectionStateDto, ConflictError);
[GenerateOneOf]
public partial class StartCollectionResult : OneOfBase<CollectionStateDto, ConflictError>;
