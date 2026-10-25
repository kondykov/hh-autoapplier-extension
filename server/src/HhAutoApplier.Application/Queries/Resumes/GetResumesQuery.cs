using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Queries.Resumes;

// Свои резюме; администратор — резюме пользователя UserId
public record GetResumesQuery(Guid? UserId = null);

// На .NET 11: public union GetResumesResult(List<ResumeDto>, ForbiddenError);
[GenerateOneOf]
public partial class GetResumesResult : OneOfBase<List<ResumeDto>, ForbiddenError>;