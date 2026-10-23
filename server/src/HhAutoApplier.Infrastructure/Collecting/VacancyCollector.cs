using HhAutoApplier.Core.Domain;
using HhAutoApplier.Core.Rules;
using HhAutoApplier.Infrastructure.Persistence;
using HhAutoApplier.Infrastructure.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HhAutoApplier.Infrastructure.Collecting;

public class CollectorOptions
{
    public const string Section = "Collector";
    public int MaxPages { get; set; } = 3;
    // Пауза между запросами к источнику — случайная в этом диапазоне, чтобы не долбить сайт ровным ритмом
    public int DelayMinMs { get; set; } = 2000;
    public int DelayMaxMs { get; set; } = 5000;
    public int IntervalMinutes { get; set; } = 60;
    public bool CollectOnStartup { get; set; } = true;
}

public record CollectResult(int Searches, int Seen, int Added, int Queued, int Skipped);

public class VacancyCollector(
    AppDbContext db,
    IEnumerable<IVacancySource> sources,
    IOptions<CollectorOptions> options,
    ILogger<VacancyCollector> log)
{
    private readonly CollectorOptions _opt = options.Value;
    private readonly VacancyEvaluator _evaluator = VacancyEvaluator.Default;

    public async Task<CollectResult> RunAsync(CancellationToken ct)
    {
        var searches = await db.Searches.Where(s => s.Enabled).ToListAsync(ct);
        var resumes = await db.Resumes.ToListAsync(ct);
        int seen = 0, added = 0, queued = 0, skipped = 0;

        foreach (var source in sources)
        foreach (var search in searches)
        {
            for (var page = 0; page < _opt.MaxPages; page++)
            {
                if (page > 0) await PauseAsync(ct);
                var found = await source.SearchAsync(search, page, ct);
                if (found.Count == 0) break;
                seen += found.Count;

                var ids = found.Select(v => v.Id).ToList();
                var known = (await db.Vacancies.Where(v => ids.Contains(v.Id)).Select(v => v.Id).ToListAsync(ct)).ToHashSet();
                var fresh = found.DistinctBy(v => v.Id).Where(v => !known.Contains(v.Id)).ToList();
                foreach (var vacancy in fresh)
                {
                    await PauseAsync(ct);
                    if (!await source.LoadDetailsAsync(vacancy, ct)) continue;

                    var eval = _evaluator.Evaluate(vacancy, search.Rules, resumes);
                    (vacancy.Status, vacancy.Score, vacancy.StatusReason, vacancy.ResumeProfile) =
                        (eval.Status, eval.Score, eval.Reason, eval.ResumeProfile);
                    db.Vacancies.Add(vacancy);
                    added++;
                    if (eval.Status == VacancyStatus.Queued) queued++; else skipped++;
                }
                await db.SaveChangesAsync(ct);
                // Выдача отсортирована по дате: страница без новых — дальше только известные
                if (fresh.Count == 0) break;
            }
        }

        log.LogInformation("Сбор: источников {Src}, поисков {S}, просмотрено {Seen}, новых {Added} (в очередь {Q}, отсеяно {Sk})",
            sources.Count(), searches.Count, seen, added, queued, skipped);
        return new CollectResult(searches.Count, seen, added, queued, skipped);
    }

    private Task PauseAsync(CancellationToken ct) =>
        Task.Delay(Random.Shared.Next(_opt.DelayMinMs, Math.Max(_opt.DelayMinMs, _opt.DelayMaxMs) + 1), ct);
}

public class CollectorHostedService(IServiceScopeFactory scopes, IOptions<CollectorOptions> options, ILogger<CollectorHostedService> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var opt = options.Value;
        if (!opt.CollectOnStartup) await Task.Delay(TimeSpan.FromMinutes(opt.IntervalMinutes), stop);
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<VacancyCollector>().RunAsync(stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (SourceBlockedException e)
            {
                log.LogWarning("{Message}", e.Message);
            }
            catch (Exception e)
            {
                log.LogError(e, "Сбор вакансий завершился ошибкой");
            }
            await Task.Delay(TimeSpan.FromMinutes(opt.IntervalMinutes), stop);
        }
    }
}
