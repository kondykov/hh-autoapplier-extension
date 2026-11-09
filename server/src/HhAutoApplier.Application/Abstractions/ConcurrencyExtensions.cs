using HhAutoApplier.Domain.Applications;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Abstractions;

public static class ConcurrencyExtensions
{
    // Сохранить результат LLM по отклику (#54). Пока LLM отвечала, отклик изменили (пропустили, одобрили, отправили) —
    // конфликт по xmin: изменения конвейера в отклике отбрасываются, остальное (журнал LlmCalls) сохраняется. false — отброшено
    public static async Task<bool> SaveOrDiscardAsync(this IAppDbContext db, JobApplication application, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Отклик и его jsonb (анкета, история — и прежние элементы: MoveTo пересобирает список) больше не отслеживаются
            foreach (var entry in db.ChangeTracker.Entries().Where(e => ReferenceEquals(e.Entity, application) || OwnedBy(e, application)).ToList())
                entry.State = EntityState.Detached;
            await db.SaveChangesAsync(ct);
            return false;
        }
    }

    private static bool OwnedBy(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, JobApplication application) =>
        entry.Metadata.FindOwnership() is { } ownership
        && ownership.PrincipalEntityType.ClrType == typeof(JobApplication)
        && Equals(entry.Property(ownership.Properties[0].Name).CurrentValue, application.Id);
}
