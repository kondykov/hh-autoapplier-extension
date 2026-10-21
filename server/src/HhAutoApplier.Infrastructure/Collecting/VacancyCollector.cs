using HhAutoApplier.Core.Domain;
using HhAutoApplier.Core.Rules;
using HhAutoApplier.Infrastructure.Hh;
using HhAutoApplier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HhAutoApplier.Infrastructure.Collecting;

public record CollectResult(int Searches, int Seen, int Added, int Queued, int Skipped);

public class VacancyCollector(
    AppDbContext db,
    IVacancySource source,
    IOptions<HhOptions> options,
    ILogger<VacancyCollector> log)
{
    private readonly HhOptions _opt = options.Value;
    private readonly VacancyEvaluator _evaluator = VacancyEvaluator.Default;

    public async Task<CollectResult> RunAsync(CancellationToken ct)
    {
        var searches = await db.Searches.Where(s => s.Enabled).ToListAsync(ct);
        var resumes = await db.Resumes.ToListAsync(ct);
        int seen = 0, added = 0, queued = 0, skipped = 0;

        foreach (var search in searches)
        {
            for (var page = 0; page < _opt.MaxPages; page++)
            {
                var ids = await source.SearchIdsAsync(search, page, ct);
                if (ids.Count == 0) break;
                seen += ids.Count;

                var known = await db.Vacancies.Where(v => ids.Contains(v.Id)).Select(v => v.Id).ToListAsync(ct);
                foreach (var id in ids.Except(known))
                {
                    await Task.Delay(_opt.RequestDelayMs, ct);
                    var vacancy = await source.GetAsync(id, search.Name, ct);
                    if (vacancy is null) continue;

                    var eval = _evaluator.Evaluate(vacancy, search.Rules, resumes);
                    vacancy.Status = eval.Status;
                    vacancy.Score = eval.Score;
                    vacancy.StatusReason = eval.Reason;
                    vacancy.ResumeProfile = eval.ResumeProfile;
                    db.Vacancies.Add(vacancy);
                    added++;
                    if (eval.Status == VacancyStatus.Queued) queued++; else skipped++;
                }
                await db.SaveChangesAsync(ct);
                if (ids.Except(known).Count() == 0) break;
                await Task.Delay(_opt.RequestDelayMs, ct);
            }
        }

        log.LogInformation("Сбор: поисков {S}, просмотрено {Seen}, новых {Added} (в очередь {Q}, отсеяно {Sk})",
            searches.Count, seen, added, queued, skipped);
        return new CollectResult(searches.Count, seen, added, queued, skipped);
    }
}

public class CollectorHostedService(IServiceScopeFactory scopes, IOptions<HhOptions> options, ILogger<CollectorHostedService> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var opt = options.Value;
        if (!opt.CollectOnStartup) await Task.Delay(TimeSpan.FromMinutes(opt.CollectIntervalMinutes), stop);
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<VacancyCollector>().RunAsync(stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                log.LogError(e, "Сбор вакансий завершился ошибкой");
            }
            await Task.Delay(TimeSpan.FromMinutes(opt.CollectIntervalMinutes), stop);
        }
    }
}
