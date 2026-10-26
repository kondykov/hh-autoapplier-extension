using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Queries.Searches;

// Свои поиски; администратор — поиски пользователя UserId
public record GetSearchesQuery(Guid? UserId = null);

// На .NET 11: public union GetSearchesResult(List<SearchDto>, ForbiddenError);
[GenerateOneOf]
public partial class GetSearchesResult : OneOfBase<List<SearchDto>, ForbiddenError>;