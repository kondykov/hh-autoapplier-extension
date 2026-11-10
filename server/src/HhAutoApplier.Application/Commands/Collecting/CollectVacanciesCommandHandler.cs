using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Application.Cqrs;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Errors;
using HhAutoApplier.Domain.Applications;
using HhAutoApplier.Domain.Resumes;
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
    CollectionProgress progress,
    ILogger<CollectVacanciesCommandHandler> logger
) : ICommandHandler<CollectVacanciesCommand, CollectVacanciesResult>
{
    public const string FailedMessage = "сбор завершился ошибкой, подробности в журнале сервера";

    // Сколько новых откликов может пропасть при сбое: загрузка страницы выдачи занимает минуты
    private const int SaveEvery = 10;

    // Сколько вакансия должна не встречаться, чтобы её проверить, и как часто проверять снова (#70)
    private static readonly TimeSpan ArchiveCheckAfter = TimeSpan.FromHours(24);

    // Незавершённые отклики, которые может закрыть уход вакансии в архив; New закрывает подтверждение карточки
    private static readonly VacancyStatus[] AwaitingStatuses =
        [VacancyStatus.Pending, VacancyStatus.Review, VacancyStatus.Questionnaire, VacancyStatus.Queued];

    private readonly CollectorOptions _opt = options.Value;

    public async Task<CollectVacanciesResult> Handle(CollectVacanciesCommand command, CancellationToken ct = default)
    {
        if (!command.Reserved && !progress.TryBegin(command.Trigger))
            return new ConflictError("Сбор уже идёт");

        try
        {
            var result = await CollectAsync(ct);
            if (result.Value is CollectResultDto done)
                progress.Finish(done);
            else if (result.Value is Error error)
                progress.Fail(error.Message);

            return result;
        }
        catch (OperationCanceledException)
        {
            progress.Fail("сбор прерван");
            throw;
        }
        catch (Exception)
        {
            progress.Fail(FailedMessage);
            throw;
        }
    }

    private async Task<CollectVacanciesResult> CollectAsync(CancellationToken ct)
    {
        // Поиски всех пользователей; отклик получает владелец поиска — с его правилами и резюме
        // Заблокированных пропускаем: ни запросов к источнику, ни LLM за их счёт
        var blocked = await users.GetBlockedIdsAsync(ct);
        var searches = (await db.Searches.AsNoTracking().Where(s => s.Enabled).ToListAsync(ct))
            .Where(s => s.UserId is not { } owner || !blocked.Contains(owner)).ToList();
        var resumes = (await db.Resumes.AsNoTracking().ToListAsync(ct)).ToLookup(r => r.UserId);
        int seen = 0, added = 0, queued = 0, skipped = 0;

        // Лимит подтверждений — общий на проход: и встреченные в выдаче карточки, и ждущие отклики (#59)
        var confirmations = _opt.MaxConfirmations;
        // Страница вакансии не открылась в этом проходе: второй запрос не поможет, отклики на неё закроет EvaluateConfirmedAsync
        var unavailable = new HashSet<string>();
        // Страница показала архив: карточка принята, отклики закрываются без оценки и запросов к LLM
        var archived = new HashSet<string>();

        try
        {
            foreach (var source in sources)
            foreach (var search in searches)
            {
                for (var page = 0; page < _opt.MaxPages; page++)
                {
                    if (page > 0)
                        await PauseAsync(ct);

                    progress.At(search.Name, page + 1, searches.Count);

                    // Листаем до конца выдачи (пустая страница): hh.ru показывает часть новых вакансий с задержкой,
                    // и они оказываются ниже уже известных — страница без новых не значит, что дальше их нет (#73)
                    var found = await source.SearchAsync(search, page, ct);
                    if (found.Count == 0)
                        break;

                    seen += found.Count;

                    // Вакансия общая: известную не загружаем заново. Новое — то, на что у владельца поиска ещё нет отклика
                    var ids = found.Select(v => v.Id).ToList();
                    var known = await db.Vacancies.Where(v => ids.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);

                    // Запросов к источнику нет: карточка уже загружена. Отклик есть или нет — вакансия встретилась
                    var seenAt = clock.GetUtcNow();
                    foreach (var vacancy in known.Values)
                        vacancy.MarkSeen(seenAt);

                    // Карточки из импорта источник подтверждает сам: данные клиента не идут дальше в чужие отклики и промпты.
                    // Подробности не загрузились — карточка остаётся импортной; лимит исчерпан — подтвердится в следующих проходах
                    var unconfirmed = found.DistinctBy(v => v.Id)
                        .Where(v => known.TryGetValue(v.Id, out var k) && k.Imported && !unavailable.Contains(v.Id))
                        .Take(Math.Max(confirmations, 0)).ToList();
                    foreach (var card in unconfirmed)
                    {
                        confirmations--;
                        await PauseAsync(ct);
                        // Временная ошибка: карточка остаётся импортной, отклик ждёт
                        var state = await source.LoadDetailsAsync(card, ct);
                        if (state is VacancyPageState.Available or VacancyPageState.Archived)
                            known[card.Id].AcceptSourceCard(card);
                        if (state == VacancyPageState.Archived)
                            archived.Add(card.Id);
                        else if (state == VacancyPageState.Unavailable)
                            unavailable.Add(card.Id);
                    }

                    var applied = (await db.JobApplications.Where(a => a.UserId == search.UserId && ids.Contains(a.VacancyId))
                        .Select(a => a.VacancyId).ToListAsync(ct)).ToHashSet();
                    var fresh = found.DistinctBy(v => v.Id).Where(v => !applied.Contains(v.Id)).ToList();
                    var unsavedFrom = added;
                    var saved = true;
                    foreach (var card in fresh)
                    {
                        progress.Count(seen, added, queued, skipped);
                        if (added - unsavedFrom >= SaveEvery)
                        {
                            saved = await TrySaveAsync(search.Id, page, ct);
                            if (!saved)
                                break;

                            unsavedFrom = added;
                        }

                        if (!known.TryGetValue(card.Id, out var vacancy))
                        {
                            await PauseAsync(ct);
                            var state = await source.LoadDetailsAsync(card, ct);
                            if (state is VacancyPageState.Unavailable or VacancyPageState.TemporaryError)
                                continue;

                            if (state == VacancyPageState.Archived)
                                archived.Add(card.Id);

                            vacancy = card;
                            db.Vacancies.Add(vacancy);
                        }

                        var now = clock.GetUtcNow();
                        var application = JobApplication.Create(vacancy, search.UserId, now, search.Id);
                        db.JobApplications.Add(application);
                        added++;

                        // Запись остаётся: иначе архивная карточка запрашивалась бы каждый проход
                        if (archived.Contains(vacancy.Id))
                        {
                            application.CloseVacancyArchived(now);
                            skipped++;
                            continue;
                        }

                        // Карточка из импорта не подтвердилась: отклик ждёт в New, его оценит EvaluateConfirmedAsync
                        if (vacancy.Imported)
                            continue;

                        application.Evaluate(evaluator, search.Rules, resumes[search.UserId], now, awaitLlm: llm.IsConfigured);
                        if (application.Status is VacancyStatus.Queued or VacancyStatus.Pending)
                            queued++;
                        else
                            skipped++;
                    }

                    // Импорт того же пользователя мог успеть создать тот же отклик (уникальный индекс): страницу пропускаем,
                    // остальные поиски идут дальше; пропущенное найдётся при следующем сборе
                    if (!saved || !await TrySaveAsync(search.Id, page, ct))
                        break;

                    progress.Count(seen, added, queued, skipped);
                }
            }

            await EvaluateConfirmedAsync(blocked, resumes, confirmations, unavailable, archived, ct);
            await CheckArchiveAsync(blocked, ct);
        }
        catch (SourceBlockedException e)
        {
            // Найденное до блокировки сохраняем, остальное — при следующем запуске
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException saveError)
            {
                db.ChangeTracker.Clear();
                logger.LogWarning(saveError, "Сбор: найденное до остановки не сохранено");
            }

            logger.LogWarning("{Message}. Новых до остановки: {Added}", e.Message, added);
            return new SourceUnavailableError(e.Message);
        }

        logger.LogInformation("Сбор: источников {Src}, поисков {S}, просмотрено {Seen}, новых {Added} (в очередь {Q}, отсеяно {Sk})",
            sources.Count(), searches.Count, seen, added, queued, skipped);
        return new CollectResultDto(searches.Count, seen, added, queued, skipped);
    }

    // Отклики на неподтверждённую импортную карточку ждут в New (ImportVacanciesCommandHandler). Источник подтверждает карточку
    // по странице вакансии — и только тогда отклик оценивается правилами своего поиска; страница недоступна или вакансия в архиве — отклик в Skipped.
    // Самые старые — первыми: и подтверждённые, и закрытые уходят из New, поэтому очередь двигается и при лимите за проход
    private async Task EvaluateConfirmedAsync(IReadOnlyList<Guid> blocked, ILookup<Guid?, ResumeProfile> resumes, int confirmations,
        HashSet<string> unavailable, HashSet<string> archived, CancellationToken ct)
    {
        var waiting = await db.ActiveApplications(blocked).Where(a => a.Status == VacancyStatus.New).ToListAsync(ct);
        foreach (var group in waiting.GroupBy(a => a.Vacancy).OrderBy(g => g.Min(a => a.CreatedAt)).ThenBy(g => g.Min(a => a.Id)))
        {
            var vacancy = group.Key;
            var state = archived.Contains(vacancy.Id) ? VacancyPageState.Archived
                : vacancy.Imported && unavailable.Contains(vacancy.Id) ? VacancyPageState.Unavailable
                : VacancyPageState.Available;
            if (vacancy.Imported && state == VacancyPageState.Available)
            {
                // Источников нет (Hh:Enabled = false) — проверить нечем, это не недоступность вакансии
                if (confirmations <= 0 || !sources.Any())
                    continue;

                confirmations--;
                state = await ConfirmAsync(vacancy, ct);
                if (state == VacancyPageState.TemporaryError)
                {
                    logger.LogWarning("Сбор: страница вакансии {Vacancy} временно недоступна, отклик ждёт", vacancy.Id);
                    continue;
                }
            }

            var now = clock.GetUtcNow();
            foreach (var application in group)
            {
                if (state == VacancyPageState.Archived)
                    application.CloseVacancyArchived(now);
                else if (state == VacancyPageState.Unavailable)
                    application.MarkVacancyUnavailable(now);
                else
                    application.Evaluate(evaluator, application.Rules, resumes[application.UserId], now, awaitLlm: llm.IsConfigured);
            }

            // По одной вакансии: пока идут запросы, отклик могут изменить (xmin), и конфликт не должен стоить остальных результатов.
            // После конфликта отслеживание сброшено: оставшееся — в следующем проходе
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException e)
            {
                db.ChangeTracker.Clear();
                logger.LogWarning(e, "Сбор: подтверждение вакансии {Vacancy} не сохранено", vacancy.Id);
                return;
            }
        }
    }

    // Вакансия могла уйти в архив, пока отклик ждал (#70). Кандидаты — только не встречавшиеся в выдаче дольше порога и не проверенные
    // за это время: сначала непроверенные, потом самые давние, поэтому следующий проход берёт другие вакансии.
    // Вакансия общая — один запрос на всех пользователей
    private async Task CheckArchiveAsync(IReadOnlyList<Guid> blocked, CancellationToken ct)
    {
        // Источников нет — проверить нечем, это не недоступность вакансии
        if (_opt.MaxArchiveChecks <= 0 || !sources.Any())
            return;

        var due = clock.GetUtcNow() - ArchiveCheckAfter;
        var awaiting = db.ActiveApplications(blocked).Where(a => AwaitingStatuses.Contains(a.Status));
        var ids = await db.Vacancies.AsNoTracking()
            .Where(v => v.LastSeenAt < due && (v.ArchiveCheckedAt == null || v.ArchiveCheckedAt < due) && awaiting.Any(a => a.VacancyId == v.Id))
            .OrderBy(v => v.ArchiveCheckedAt != null).ThenBy(v => v.ArchiveCheckedAt).ThenBy(v => v.LastSeenAt).ThenBy(v => v.Id)
            .Take(_opt.MaxArchiveChecks).Select(v => v.Id).ToListAsync(ct);
        if (ids.Count == 0)
            return;

        var applications = await awaiting.Where(a => ids.Contains(a.VacancyId)).ToListAsync(ct);
        foreach (var group in applications.GroupBy(a => a.Vacancy).OrderBy(g => ids.IndexOf(g.Key.Id)))
        {
            var vacancy = group.Key;
            var (state, _) = await LoadPageAsync(vacancy, ct);
            // Отметки нет: о вакансии ничего не узнали, проверка повторится в следующем проходе
            if (state == VacancyPageState.TemporaryError)
            {
                logger.LogWarning("Сбор: страница вакансии {Vacancy} временно недоступна, проверка архива отложена", vacancy.Id);
                continue;
            }

            var now = clock.GetUtcNow();
            vacancy.MarkArchiveChecked(now);
            foreach (var application in group)
            {
                if (state == VacancyPageState.Archived)
                    application.CloseVacancyArchived(now);
                else if (state == VacancyPageState.Unavailable)
                    application.CloseVacancyUnavailable(now);
            }

            // По одной вакансии, как при подтверждении: отправленный за время запроса отклик конфликтует по xmin и остаётся как есть
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException e)
            {
                db.ChangeTracker.Clear();
                logger.LogWarning(e, "Сбор: проверка архива вакансии {Vacancy} не сохранена", vacancy.Id);
                return;
            }
        }
    }

    // Импортная карточка заменяется данными источника и у архивной вакансии: страница у неё та же, а текст клиента дальше идти не должен
    private async Task<VacancyPageState> ConfirmAsync(Vacancy vacancy, CancellationToken ct)
    {
        var (state, card) = await LoadPageAsync(vacancy, ct);
        if (card is not null)
            vacancy.AcceptSourceCard(card);
        return state;
    }

    // Первый источник, у которого открылась страница вакансии; ни у одного — Unavailable, а если кто-то ответил ошибкой — TemporaryError.
    // Капча и отказы — SourceBlockedException: проход останавливается, отклик ждёт следующего
    private async Task<(VacancyPageState State, Vacancy? Card)> LoadPageAsync(Vacancy vacancy, CancellationToken ct)
    {
        var missing = VacancyPageState.Unavailable;
        foreach (var source in sources)
        {
            var card = Vacancy.Create(vacancy.Id, "", "", vacancy.SearchName, clock.GetUtcNow());
            await PauseAsync(ct);
            var state = await source.LoadDetailsAsync(card, ct);
            if (state is VacancyPageState.Available or VacancyPageState.Archived)
                return (state, card);
            if (state == VacancyPageState.TemporaryError)
                missing = state;
        }

        return (missing, null);
    }

    private async Task<bool> TrySaveAsync(int searchId, int page, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException e)
        {
            db.ChangeTracker.Clear();
            logger.LogWarning(e, "Сбор: страница {Page} поиска {Search} не сохранена", page, searchId);
            return false;
        }
    }

    private Task PauseAsync(CancellationToken ct) =>
        Task.Delay(Random.Shared.Next(_opt.DelayMinMs, Math.Max(_opt.DelayMinMs, _opt.DelayMaxMs) + 1), ct);
}
