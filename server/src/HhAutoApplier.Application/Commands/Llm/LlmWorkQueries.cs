using HhAutoApplier.Domain.Applications;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Application.Commands.Llm;

internal static class LlmWorkQueries
{
    public static async Task<List<JobApplication>> TakePerUserAsync(
        IQueryable<JobApplication> applications,
        int limit,
        Func<IQueryable<JobApplication>, IOrderedQueryable<JobApplication>> orderBy,
        CancellationToken ct
    )
    {
        if (limit <= 0)
            return [];

        var userIds = await applications.Select(application => application.UserId).Distinct().ToListAsync(ct);
        var candidates = new List<JobApplication>();
        foreach (var userId in userIds)
        {
            // Группировка запроса с Include не переводится EF, поэтому ограничиваем выборку отдельным запросом владельца.
            candidates.AddRange(await orderBy(applications.Where(application => application.UserId == userId))
                .Take(limit).ToListAsync(ct));
        }

        return candidates;
    }
}
