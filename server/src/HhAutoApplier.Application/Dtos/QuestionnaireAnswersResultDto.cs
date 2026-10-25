namespace HhAutoApplier.Application.Dtos;

// Failed — ответ LLM неполный или с несуществующим вариантом (вакансия пропущена с причиной); StoppedBy — почему остановились раньше
public record QuestionnaireAnswersResultDto(int Answered, int Failed, int Remaining, string? StoppedBy);
