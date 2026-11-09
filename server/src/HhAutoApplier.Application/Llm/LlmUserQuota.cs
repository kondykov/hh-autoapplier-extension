using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Notifications;
using HhAutoApplier.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HhAutoApplier.Application.Llm;

// Доля дневного лимита LLM на пользователя (#56): один пользователь или утёкший токен не расходует лимит за всех.
// Считается по журналу LlmCalls за сутки UTC, как общий лимит. Записи без модели не считаются: запрос не получил
// ответа — при сбое провайдера запись появляется каждый проход, и доля кончилась бы без единого ответа
public class LlmUserQuota(IAppDbContext db, IOptions<LlmOptions> options, Notifier notifier, TimeProvider clock)
{
    public async Task<LlmQuotaUsage> LoadAsync(CancellationToken ct)
    {
        var today = new DateTimeOffset(clock.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);
        var used = await db.LlmCalls.Where(c => c.CreatedAt >= today && c.Model != "")
            .GroupBy(c => c.UserId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        return new LlmQuotaUsage(options.Value.UserDailyRequestLimit, used.Select(u => (u.Key, u.Count)));
    }

    // Отклики сверх доли молча ждут следующих суток — пользователю одно сообщение в сутки
    public async Task NotifyExhaustedAsync(CancellationToken ct)
    {
        var usage = await LoadAsync(ct);
        foreach (var user in usage.Exhausted.OfType<Guid>())
            await notifier.NotifyAsync(user, NotificationKind.LlmUnavailable, $"llm-quota:{clock.GetUtcNow():yyyy-MM-dd}",
                $"Исчерпана ваша доля запросов к LLM за сутки ({options.Value.UserDailyRequestLimit}). Оценка, анкеты, письма и разбор ответов продолжатся завтра.", null, ct);
    }
}

// Расход за сутки на один проход конвейера. limit 0 — без доли. UserId null — владелец по умолчанию, у него своя доля
public class LlmQuotaUsage
{
    private readonly int _limit;

    // Ключ словаря не может быть null: владелец по умолчанию считается отдельно
    private readonly Dictionary<Guid, int> _used = [];

    private int _usedByDefaultOwner;

    public LlmQuotaUsage(int limit, IEnumerable<(Guid? UserId, int Count)> used)
    {
        _limit = limit;
        foreach (var (userId, count) in used)
        {
            if (userId is { } id)
                _used[id] = count;
            else
                _usedByDefaultOwner = count;
        }

        Exhausted = [.. _used.Keys.Where(id => IsExhausted(id)).Select(id => (Guid?)id), .. IsExhausted(null) ? [(Guid?)null] : Array.Empty<Guid?>()];
    }

    // Кто исчерпал долю к началу прохода: их отклики не попадают в выборку, иначе заняли бы всю пачку и остальные бы ждали
    public IReadOnlyList<Guid?> Exhausted { get; }

    public bool IsExhausted(Guid? userId) => _limit > 0 && Used(userId) >= _limit;

    // Перед каждым запросом к LLM; false — доля кончилась за этот проход
    public bool TryTake(Guid? userId)
    {
        if (IsExhausted(userId))
            return false;

        if (userId is { } id)
            _used[id] = Used(id) + 1;
        else
            _usedByDefaultOwner++;
        return true;
    }

    private int Used(Guid? userId) => userId is { } id ? _used.GetValueOrDefault(id) : _usedByDefaultOwner;
}
