namespace HhAutoApplier.Application.Dtos;

// Failed — ответ LLM не похож на письмо (вакансия ждёт следующего запуска); StoppedBy — почему остановились раньше
public record CoverLettersResultDto(int Written, int Failed, int Remaining, string? StoppedBy);
