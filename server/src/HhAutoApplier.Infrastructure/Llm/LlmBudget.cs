namespace HhAutoApplier.Infrastructure.Llm;

// Счётчик запросов к LLM за сутки (UTC). usedToday — сколько уже было в журнале LlmCalls: после перезапуска счёт продолжается
public class LlmBudget(TimeProvider clock, Func<DateOnly, int>? usedToday = null)
{
    private readonly object _lock = new();

    private DateOnly _day;

    private int _used;

    public int Used
    {
        get
        {
            lock (_lock)
            {
                Roll();
                return _used;
            }
        }
    }

    // limit 0 — без ограничения
    public bool TryTake(int limit)
    {
        lock (_lock)
        {
            Roll();
            if (limit > 0 && _used >= limit)
                return false;

            _used++;
            return true;
        }
    }

    private void Roll()
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (today == _day)
            return;

        _day = today;
        _used = usedToday?.Invoke(today) ?? 0;
    }
}
