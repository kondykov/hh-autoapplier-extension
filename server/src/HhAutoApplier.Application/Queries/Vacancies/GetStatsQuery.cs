using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Queries.Vacancies;

// Число своих откликов в каждом статусе; администратор — пользователя UserId
public record GetStatsQuery(Guid? UserId = null);

// На .NET 11: public union GetStatsResult(Dictionary<string, int>, ForbiddenError);
[GenerateOneOf]
public partial class GetStatsResult : OneOfBase<Dictionary<string, int>, ForbiddenError>;