using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain.Applications;
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
    TimeProvider clock,
    IUserService users,
    ILogger<CollectVacanciesCommandHandler> logger
) : ICommandHandler<CollectVacanciesCommand, CollectVacanciesResult>
{
    private readonly CollectorOptions _opt = options.Value;

    public async Task<CollectVacanciesResult> Handle(CollectVacanciesCommand command, CancellationToken ct = default)
    {
        // Поиски всех пользователей; отклик получает владелец поиска — с его правилами и резюме
        // Заблокированных пропускаем: ни запросов к источнику, ни LLM за их счёт
        var blocked = await users.GetBlockedIdsAsync(ct);
        var searches = (await db.Searches.AsNoTracking().Where(s => s.Enabled).ToListAsync(ct))
            .Where(s => s.UserId is not { } owner || !blocked.Contains(owner)).ToList();
        var resumes = (await db.Resumes.AsNoTracking().ToListAsync(ct)).ToLookup(r => r.UserId);
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

                    // Вакансия общая: известную не загружаем заново. Новое — то, на что у владельца поиска ещё нет отклика
                    var ids = found.Select(v => v.Id).ToList();
                    var known = await db.Vacancies.Where(v => ids.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);

                    // Карточки из импорта источник подтверждает сам: данные клиента не идут дальше в чужие отклики и промпты
                    foreach (var card in found.DistinctBy(v => v.Id).Where(v => known.TryGetValue(v.Id, out var k) && k.Imported))
                    {
                        var imported = known[card.Id];
                        imported.AcceptSourceCard(card);
                        await PauseAsync(ct);
                        await source.LoadDetailsAsync(imported, ct);
                    }

                    var applied = (await db.JobApplications.Where(a => a.UserId == search.UserId && ids.Contains(a.VacancyId))
                        .Select(a => a.VacancyId).ToListAsync(ct)).ToHashSet();
                    var fresh = found.DistinctBy(v => v.Id).Where(v => !applied.Contains(v.Id)).ToList();
                    foreach (var card in fresh)
                    {
                        if (!known.TryGetValue(card.Id, out var vacancy))
                        {
                            await PauseAsync(ct);
                            if (!await source.LoadDetailsAsync(card, ct))
                                continue;

                            vacancy = card;
                            db.Vacancies.Add(vacancy);
                        }

                        var now = clock.GetUtcNow();
                        var application = JobApplication.Create(vacancy, search.UserId, now, search.Id);
                        application.Evaluate(evaluator, search.Rules, resumes[search.UserId], now, awaitLlm: llm.IsConfigured);
                        db.JobApplications.Add(application);
                        added++;
                        if (application.Status is VacancyStatus.Queued or VacancyStatus.Pending)
                            queued++;
                        else
                            skipped++;
                    }

                    // Импорт того же пользователя мог успеть создать тот же отклик (уникальный индекс): страницу пропускаем,
                    // остальные поиски идут дальше; пропущенное найдётся при следующем сборе
                    try
                    {
                        await db.SaveChangesAsync(ct);
                    }
                    catch (DbUpdateException e)
                    {
                        db.ChangeTracker.Clear();
                        logger.LogWarning(e, "Сбор: страница {Page} поиска {Search} не сохранена", page, search.Id);
                        break;
                    }

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
