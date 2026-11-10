using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using OneOf;

namespace HhAutoApplier.Application.Commands.Collecting;

// Пройти все включённые поиски по всем источникам. Запускается по расписанию и вручную.
// Reserved — запуск уже занят в CollectionProgress тем, кто запустил проход в фоне
public record CollectVacanciesCommand(string Trigger = CollectionProgress.Schedule, bool Reserved = false);

// На .NET 11: public union CollectVacanciesResult(CollectResultDto, SourceUnavailableError, ConflictError);
[GenerateOneOf]
public partial class CollectVacanciesResult : OneOfBase<CollectResultDto, SourceUnavailableError, ConflictError>;
