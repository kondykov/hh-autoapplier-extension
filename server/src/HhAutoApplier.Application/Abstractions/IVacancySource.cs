using HhAutoApplier.Domain.Searches;
using HhAutoApplier.Domain.Vacancies;

namespace HhAutoApplier.Application.Abstractions;

// Агрегатор вакансий: hh.ru, позже другие. Поиск дешёвый (карточки с выдачи), детали — по запросу только для новых
public interface IVacancySource
{
    string Name { get; }

    Task<IReadOnlyList<Vacancy>> SearchAsync(SearchProfile search, int page, CancellationToken ct);

    // Дополняет вакансию описанием и навыками; false — вакансия недоступна (удалена, в архиве)
    Task<bool> LoadDetailsAsync(Vacancy vacancy, CancellationToken ct);
}

// Источник перестал отдавать данные: капча, 403/429 или неожиданная страница. Сбор останавливается до следующего запуска
public class SourceBlockedException(string source, string reason) : Exception($"{source}: сбор остановлен — {reason}. Повтор при следующем запуске");
