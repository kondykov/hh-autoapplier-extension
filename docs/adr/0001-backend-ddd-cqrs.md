# ADR 0001. Backend: DDD и простой CQRS без MediatR

Статус: **предложено** (ждёт решения владельца). Дата: 2026-10-23. Issue: #3, #11.

## Контекст

Backend вырос из прототипа: вся логика эндпоинтов — в `Program.cs` (minimal API), сущности — открытые классы с публичными сеттерами, правила — статические функции в `Core`. Код работает, но:
- непонятно, где искать сценарий (импорт, смена статуса, отчёт) — всё в одном файле;
- инварианты не защищены: статус, балл и дату отклика может поменять кто угодно;
- впереди LLM, пользователи и RBAC, новые агрегаторы — без структуры это расползётся.

Цель — архитектура, в которой владелец сам продолжит разработку: каждый сценарий в своём файле, правила в домене, понятные слои. За образец взят `Pp.HelperCrm.OrgsApi`.

## Что берём из образца

- `ICommandHandler<TCommand, TResult>` и `IQueryHandler<TQuery, TResult>` с одним методом `Handle`; без MediatR и без диспетчера.
- Команда/запрос — `record` с DataAnnotations; обработчик — отдельный класс рядом: `Application/Commands/<Фича>/XxxCommand.cs` + `XxxCommandHandler.cs`.
- Контроллер получает обработчик через `[FromServices]` и разбирает результат `OneOf<…>` через `Match` в HTTP-ответ.
- Ошибки — типы-записи (`NotFoundError`, `ConflictError`, …) вместо исключений для ожидаемых ситуаций.
- Маппинг сущность → DTO — экстракторы `IExtractor<TSource, TTarget>`.
- Автоматическая регистрация обработчиков и экстракторов сканированием сборки.
- Контроллеры с XML-документацией и `ProducesResponseType`, OpenAPI.

## Что добавляем: DDD

В образце модели — сгенерированные EF-классы из общего пакета, а логика живёт в обработчиках. Здесь модель своя, поэтому делаем её богатой.

**Агрегаты** (`Domain/<Агрегат>/`), сеттеры закрыты, изменения — только методами с проверкой инвариантов:

| Агрегат | Что внутри | Поведение |
|---|---|---|
| `Vacancy` | данные вакансии, `Salary` (value object), стек, статус, балл, резюме | `Evaluate(rules, resumes, evaluator)`, `MarkApplied(resume)`, `Skip(reason)`, `Fail(reason)` — переходы статусов проверяются |
| `SearchProfile` | имя, запрос, параметры источника, `FilterRules` (value object), вкл/выкл | `Update(...)`, `Enable()/Disable()` |
| `ResumeProfile` | имя, подстрока для выбора, ключевые слова, письмо | `Update(...)` |
| позже `Application` (#8) | отклик пользователя: вакансия, пользователь, балл, письмо, ответы, история | с RBAC |

**Value objects:** `Salary` (from, to, валюта; перевод в рубли), `FilterRules`, позже `VacancyKey` (источник + внешний id, #32).

**Доменные сервисы** (без EF и HTTP): `VacancyEvaluator`, `ResumePicker`, `StackExtractor` — как сейчас, переезжают в `Domain/Services`.

**Порты** (интерфейсы, которые домену/приложению нужны от внешнего мира): `IVacancySource` — в Application, реализации — в Infrastructure.

## Структура

Четыре проекта — компилятор сам не даст домену зависеть от EF или HTTP:

```
server/src/
  HhAutoApplier.Domain/                 без зависимостей
    Vacancies/      Vacancy, Salary, VacancyStatus
    Searches/       SearchProfile, FilterRules
    Resumes/        ResumeProfile
    Services/       VacancyEvaluator, ResumePicker, StackExtractor, TextNormalizer
    DomainException.cs
  HhAutoApplier.Application/            → Domain
    Cqrs/           ICommandHandler, IQueryHandler, IExtractor
    Errors/         NotFoundError, ConflictError, ValidationError
    Abstractions/   IAppDbContext (DbSet-ы), IVacancySource, SourceBlockedException
    Commands/
      Vacancies/    ImportVacancies, SetVacancyStatus
      Collecting/   CollectVacancies
      Searches/     SaveSearch, DeleteSearch
      Resumes/      SaveResume
    Queries/
      Vacancies/    GetQueue, GetVacancies, GetStats
      Searches/     GetSearches
      Resumes/      GetResumes
      Reports/      GetApplicationsReport (json/csv)
    Dtos/           VacancyDto, SearchDto, ResumeDto, StatsDto
    Extractors/     VacancyExtractor, SearchExtractor, ResumeExtractor
  HhAutoApplier.Infrastructure/         → Application
    Persistence/    AppDbContext : IAppDbContext, конфигурации сущностей, миграции, DbSeeder
    Sources/Hh/     HhWebSource, HhPageParser
    Collecting/     CollectorHostedService → вызывает CollectVacanciesCommandHandler
  HhAutoApplier.Api/                    → Infrastructure
    Controllers/    VacanciesController, SearchesController, ResumesController, ReportsController, CollectorController
    Program.cs, Dependencies.cs (сканирование обработчиков), ApiKeyMiddleware
```

Сейчас `Core` = будущий `Domain`; `Application` появляется новым проектом, логика из `Program.cs` и `VacancyCollector` расходится по обработчикам.

## Как выглядит сценарий

```csharp
// Application/Commands/Vacancies/SetVacancyStatusCommand.cs
public record SetVacancyStatusCommand
{
    [Required]
    public required string VacancyId { get; init; }

    [Required]
    public VacancyStatus Status { get; init; }

    public string? Reason { get; init; }

    public string? Resume { get; init; }
}

// Application/Commands/Vacancies/SetVacancyStatusCommandHandler.cs
public class SetVacancyStatusCommandHandler(
    IAppDbContext db,
    IExtractor<Vacancy, VacancyDto> extractor
) : ICommandHandler<SetVacancyStatusCommand, OneOf<VacancyDto, NotFoundError, ConflictError>>
{
    public async Task<OneOf<VacancyDto, NotFoundError, ConflictError>> Handle(SetVacancyStatusCommand command)
    {
        var vacancy = await db.Vacancies.FindAsync(command.VacancyId);
        if (vacancy is null) return new NotFoundError($"Вакансия {command.VacancyId} не найдена");

        switch (command.Status)
        {
            case VacancyStatus.Applied:
                vacancy.MarkApplied(command.Resume);
                break;
            case VacancyStatus.Skipped:
                vacancy.Skip(command.Reason);
                break;
            default:
                return new ConflictError($"Статус {command.Status} нельзя выставить вручную");
        }

        await db.SaveChangesAsync();
        return extractor.Extract(vacancy);
    }
}

// Api/Controllers/VacanciesController.cs
[HttpPost("{id}/status")]
public async Task<IActionResult> SetStatus(
    string id,
    [FromBody] SetVacancyStatusRequest request,
    [FromServices] SetVacancyStatusCommandHandler handler
)
{
    var result = await handler.Handle(new SetVacancyStatusCommand { VacancyId = id, Status = request.Status, Reason = request.Reason, Resume = request.Resume });
    return result.Match<IActionResult>(Ok, notFound => NotFound(notFound), conflict => Conflict(conflict));
}
```

Правило: **команды** меняют состояние через методы агрегатов; **запросы** читают `AsNoTracking` и проецируют в DTO, агрегаты не трогают.

## Решения, которые нужно подтвердить

| Вопрос | Предложение | Альтернатива |
|---|---|---|
| Проекты | 4 проекта (Domain, Application, Infrastructure, Api) | один проект с папками, как в образце |
| Результаты | `OneOf` (новая зависимость), как в образце | свой `Result<T, TError>` |
| Регистрация обработчиков | `Scrutor` (новая зависимость), как в образце | своё сканирование сборки, ~15 строк |
| HTTP | MVC-контроллеры с XML-доками, как в образце | minimal API, сгруппированный по фичам |
| OpenAPI UI | Scalar (новая зависимость) | без UI, только `/openapi/v1.json` |
| Схема БД | не меняется: `Salary` и правила маппятся на текущие колонки | — |
| API для расширения | маршруты и формат ответов сохраняются | — |

## Последствия

- Сценарий ищется по имени файла; новая функция — новая пара команда/обработчик, без правки `Program.cs`.
- Домен тестируется без БД; обработчики — на SQLite в памяти или Testcontainers (#12).
- Больше файлов — цена читаемости; для проекта такого размера приемлемо.

## План перехода

1. `Core` → `Domain`: агрегаты с закрытыми сеттерами и методами, value objects; тесты домена.
2. Проект `Application`: CQRS-абстракции, ошибки, команды и запросы для текущих эндпоинтов, экстракторы; сборщик — `CollectVacanciesCommandHandler`.
3. Infrastructure: `AppDbContext : IAppDbContext`, конфигурации сущностей в отдельных классах; hosted service вызывает обработчик.
4. Api: контроллеры вместо `Program.cs`, автосканирование, OpenAPI; прогон всех тестов и полного цикла в Docker.
