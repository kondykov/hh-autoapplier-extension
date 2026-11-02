namespace HhAutoApplier.Application.Llm;

// Один проход LLM-конвейера за раз (#54): фоновый раз в минуту и ручные /api/llm/* не обрабатывают одни и те же
// отклики параллельно — без двойных запросов к LLM. Один экземпляр api: замок в памяти процесса
public sealed class LlmPipelineGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<IDisposable> EnterAsync(CancellationToken ct)
    {
        await _semaphore.WaitAsync(ct);
        return new Release(_semaphore);
    }

    private sealed class Release(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                semaphore.Release();
        }
    }
}
