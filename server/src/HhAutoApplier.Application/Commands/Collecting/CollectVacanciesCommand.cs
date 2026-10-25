using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Commands.Collecting;

// Пройти все включённые поиски по всем источникам. Запускается по расписанию и вручную
public record CollectVacanciesCommand;

// На .NET 11: public union CollectVacanciesResult(CollectResultDto, SourceUnavailableError);
[GenerateOneOf]
public partial class CollectVacanciesResult : OneOfBase<CollectResultDto, SourceUnavailableError>;
