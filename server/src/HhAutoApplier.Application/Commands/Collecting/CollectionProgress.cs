using HhAutoApplier.Application.Dtos;

namespace HhAutoApplier.Application.Commands.Collecting;

// Состояние сбора на весь процесс: одновременно идёт один проход, страница читает, что он делает сейчас (#74).
// Хранится в памяти: после перезапуска сервера «последний сбор» неизвестен, пока не пройдёт новый
public class CollectionProgress(TimeProvider clock)
{
    public const string Schedule = "по расписанию";

    public const string Button = "по кнопке";

    public const string Api = "через API";

    private readonly Lock _gate = new();

    private CollectionRunDto? _current;

    private CollectionRunDto? _last;

    public CollectionStateDto State
    {
        get
        {
            lock (_gate)
                return new CollectionStateDto(_current, _last);
        }
    }

    // false — сбор уже идёт
    public bool TryBegin(string trigger)
    {
        lock (_gate)
        {
            if (_current is not null)
                return false;

            _current = new CollectionRunDto { Trigger = trigger, StartedAt = clock.GetUtcNow() };
            return true;
        }
    }

    public void At(string search, int page, int searches) =>
        Change(run => run with { Search = search, Page = page, Searches = searches });

    public void Count(int seen, int added, int queued, int skipped) =>
        Change(run => run with { Seen = seen, Added = added, Queued = queued, Skipped = skipped });

    public void Finish(CollectResultDto result) =>
        End(run => run with
        {
            Searches = result.Searches, Seen = result.Seen, Added = result.Added, Queued = result.Queued, Skipped = result.Skipped,
        });

    // Счётчики остаются как были на момент сбоя: найденное до него сохранено
    public void Fail(string error) => End(run => run with { Error = error });

    private void Change(Func<CollectionRunDto, CollectionRunDto> change)
    {
        lock (_gate)
        {
            if (_current is not null)
                _current = change(_current);
        }
    }

    private void End(Func<CollectionRunDto, CollectionRunDto> change)
    {
        lock (_gate)
        {
            if (_current is null)
                return;

            _last = change(_current) with { Search = null, Page = 0, FinishedAt = clock.GetUtcNow() };
            _current = null;
        }
    }
}
