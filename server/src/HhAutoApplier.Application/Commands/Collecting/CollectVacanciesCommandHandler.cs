using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain.Services;
using HhAutoApplier.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HhAutoApplier.Application.Commands.Collecting;

public class CollectVacanciesCommandHandler(
    IAppDbContext db,
    IEnumerable<IVacancySource> sources,
    ILlmClient llm,
    VacancyEvaluator evaluator,
    IOptions<CollectorOptions> options,
    ILogger<CollectVacanciesCommandHandler> logger
) : ICommandHandler<CollectVacanciesCommand, CollectVacanciesResult>
{
    private readonly CollectorOptions _opt = options.Value;

    public async Task<CollectVacanciesResult> Handle(CollectVacanciesCommand command, CancellationToken ct = default)
    {
        var searches = await db.Searches.AsNoTracking().Where(s => s.Enabled).ToListAsync(ct);
        var resumes = await db.Resumes.AsNoTracking().ToListAsync(ct);
        int seen = 0, added = 0, queued = 0, skipped = 0;

        try
        {
            foreach (var source in sources)
            foreach (var search in searches)
            {
                for (var page = 0; page < _opt.MaxPages; page++)
                {
                    if (page > 0)
                        await PauseAsync(ct);

                    var found = await source.SearchAsync(search, page, ct);
                    if (found.Count == 0)
                        break;

                    seen += found.Count;

                    var ids = found.Select(v => v.Id).ToList();
                    var known = (await db.Vacancies.Where(v => ids.Contains(v.Id)).Select(v => v.Id).ToListAsync(ct)).ToHashSet();
                    var fresh = found.DistinctBy(v => v.Id).Where(v => !known.Contains(v.Id)).ToList();
                    foreach (var vacancy in fresh)
                    {
                        await PauseAsync(ct);
                        if (!await source.LoadDetailsAsync(vacancy, ct))
                            continue;

                        vacancy.Evaluate(evaluator, search.Rules, resumes, awaitLlm: llm.IsConfigured);
                        db.Vacancies.Add(vacancy);
                        added++;
                        if (vacancy.Status is VacancyStatus.Queued or VacancyStatus.Pending)
                            queued++;
                        else
                            skipped++;
                    }

                    await db.SaveChangesAsync(ct);

                    // Выдача отсортирована по дате: страница без новых — дальше только известные
                    if (fresh.Count == 0)
                        break;
                }
            }
        }
        catch (SourceBlockedException e)
        {
            // Найденное до блокировки сохраняем, остальное — при следующем запуске
            await db.SaveChangesAsync(ct);
            logger.LogWarning("{Message}. Новых до остановки: {Added}", e.Message, added);
            return new SourceUnavailableError(e.Message);
        }

        logger.LogInformation("Сбор: источников {Src}, поисков {S}, просмотрено {Seen}, новых {Added} (в очередь {Q}, отсеяно {Sk})",
            sources.Count(), searches.Count, seen, added, queued, skipped);
        return new CollectResultDto(searches.Count, seen, added, queued, skipped);
    }

    private Task PauseAsync(CancellationToken ct) =>
        Task.Delay(Random.Shared.Next(_opt.DelayMinMs, Math.Max(_opt.DelayMinMs, _opt.DelayMaxMs) + 1), ct);
}
