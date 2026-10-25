namespace HhAutoApplier.Application.Dtos;

// Reset — сколько писем сброшено; новые напишет POST /api/llm/letters или фоновый запуск
public record ResetCoverLettersResultDto(int Reset);
