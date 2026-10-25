using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Application.Dtos;

// Вопрос анкеты работодателя; Answer пустой — ответа ещё нет
public record VacancyQuestionDto(string Question, QuestionType Type, IReadOnlyList<string> Options, IReadOnlyList<string> Answer);
