namespace HhAutoApplier.Application.Dtos;

// StoppedBy — почему оценка остановилась раньше (лимит, провайдер недоступен); null — прошла все
public record LlmEvaluationResultDto(int Evaluated, int Fit, int Doubtful, int Rejected, int Remaining, string? StoppedBy);
