namespace HhAutoApplier.Infrastructure.Llm;

// Источник короткоживущего токена доступа для провайдеров, где постоянный ключ нельзя слать как Bearer
public interface ILlmTokenSource
{
    Task<string> GetAsync(CancellationToken ct);

    // Провайдер отверг этот токен: следующий GetAsync получит новый. Токен, уже заменённый другим запросом, не сбрасывает новый
    void Invalidate(string token);
}