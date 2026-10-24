namespace HhAutoApplier.Domain.Vacancies;

// Вердикт LLM по вакансии: подходит, сомнительно (на ручную проверку, #25), не подходит
public enum LlmVerdict
{
    Fit = 0,
    Doubtful = 1,
    Reject = 2,
}
