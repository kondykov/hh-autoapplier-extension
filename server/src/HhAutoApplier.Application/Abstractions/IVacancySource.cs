using HhAutoApplier.Domain.Searches;
using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Application.Abstractions;

// Агрегатор вакансий: hh.ru, позже другие. Поиск дешёвый (карточки с выдачи), детали — по запросу только для новых
public interface IVacancySource
{
    string Name { get; }

    Task<IReadOnlyList<Vacancy>> SearchAsync(SearchProfile search, int page, CancellationToken ct);

    // Дополняет вакансию описанием и навыками (и архивную: страница у неё та же).
    // Капча, отказы и страница без данных — SourceBlockedException: о вакансии это ничего не говорит
    Task<VacancyPageState> LoadDetailsAsync(Vacancy vacancy, CancellationToken ct);
}

public enum VacancyPageState
{
    Available,

    // Работодатель убрал вакансию в архив: откликаться некуда, страница при этом открывается как обычно
    Archived,

    // Страницы нет: вакансия удалена
    Unavailable,

    // Ошибка сервера источника: о вакансии это ничего не говорит, проход идёт дальше, отклики ждут
    TemporaryError,
}

// Источник перестал отдавать данные: капча, 403/429 или неожиданная страница. Сбор останавливается до следующего запуска
public class SourceBlockedException(string source, string reason) : Exception($"{source}: сбор остановлен — {reason}. Повтор при следующем запуске");
