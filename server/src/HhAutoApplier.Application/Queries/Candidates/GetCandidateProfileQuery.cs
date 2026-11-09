using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Queries.Candidates;

// Свой профиль кандидата; администратор — профиль пользователя UserId. Профиля нет — пустой
public record GetCandidateProfileQuery(Guid? UserId = null);

// На .NET 11: public union GetCandidateProfileResult(CandidateProfileDto, ForbiddenError);
[GenerateOneOf]
public partial class GetCandidateProfileResult : OneOfBase<CandidateProfileDto, ForbiddenError>;
