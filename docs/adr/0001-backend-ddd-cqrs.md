# ADR 0001. Backend: DDD и простой CQRS без MediatR

Статус: **принято и реализовано**. Дата: 2026-10-23. Issue: #3, #11.

## Контекст

Backend вырос из прототипа: вся логика эндпоинтов — в `Program.cs` (minimal API), сущности — открытые классы с публичными сеттерами, правила — статические функции в `Core`. Код работает, но:
- непонятно, где искать сценарий (импорт, смена статуса, отчёт) — всё в одном файле;
- инварианты не защищены: статус, балл и дату отклика может поменять кто угодно;
- впереди LLM, пользователи и RBAC, новые агрегаторы — без структуры это расползётся.

Цель — архитектура, в которой владелец сам продолжит разработку: каждый сценарий в своём файле, правила в домене, понятные слои. За образец взят `Pp.HelperCrm.OrgsApi`.

## Что берём из образца

- `ICommandHandler<TCommand, TResult>` и `IQueryHandler<TQuery, TResult>` с одним методом `Handle`; без MediatR и без диспетчера.
- Команда/запрос — `record` с DataAnnotations; обработчик — отдельный класс рядом: `Application/Commands/<Фича>/XxxCommand.cs` + `XxxCommandHandler.cs`.
- Контроллер получает обработчик через `[FromServices]` и переводит результат `OneOf` в HTTP-ответ. В отличие от образца — не через `Match`, а через `.Value` в общем `FromResult` (см. «Переезд на union-типы»).
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
    Errors/         NotFoundError, ConflictError, ValidationError, SourceUnavailableError
    Abstractions/   IAppDbContext (DbSet-ы), IVacancySource, SourceBlockedException, CollectorOptions
    DependencyInjection.cs  AddApplication: Scrutor-сканирование обработчиков и экстракторов
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
    Dtos/           VacancyDto, SearchDto, ResumeDto, ImportResultDto, CollectResultDto, FileDto
    Extractors/     VacancyExtractor, SearchExtractor, ResumeExtractor
  HhAutoApplier.Infrastructure/         → Application
    Persistence/    AppDbContext : IAppDbContext, Configurations/, миграции, DbSeeder, DatabaseInitializer
    Sources/Hh/     HhWebSource, HhPageParser
    Collecting/     CollectorHostedService → вызывает CollectVacanciesCommandHandler
  HhAutoApplier.Api/                    → Infrastructure
    Controllers/    VacanciesController, SearchesController, ResumesController, ReportsController, CollectorController
    Controllers/ApiController.cs  FromResult: результат сценария → HTTP
    Middlewares/    ApiKeyMiddleware
    Program.cs
```

Прежний `Core` стал `Domain`; логика из `Program.cs` и `VacancyCollector` разошлась по обработчикам `Application`.

## Как выглядит сценарий

Пример — смена статуса вакансии (полный код в `server/src/HhAutoApplier.Application/Commands/Vacancies/`):

```csharp
// SetVacancyStatusCommand.cs — команда и её результат
public record SetVacancyStatusCommand
{
    [Required]
    public required string VacancyId { get; init; }

    [Required]
    public required VacancyStatus Status { get; init; }

    public string? Reason { get; init; }

    public string? Resume { get; init; }
}

[GenerateOneOf]
public partial class SetVacancyStatusResult : OneOfBase<VacancyDto, NotFoundError, ConflictError>;

// SetVacancyStatusCommandHandler.cs — правила перехода живут в агрегате, обработчик их вызывает
public async Task<SetVacancyStatusResult> Handle(SetVacancyStatusCommand command, CancellationToken ct = default)
{
    var vacancy = await db.Vacancies.FindAsync([command.VacancyId], ct);
    if (vacancy is null)
        return new NotFoundError($"Вакансия {command.VacancyId} не найдена");

    try
    {
        switch (command.Status)
        {
            case VacancyStatus.Applied:
                vacancy.MarkApplied(command.Resume, clock.GetUtcNow());
                break;
            // Skipped, Failed, Rejected, Queued — аналогично
            default:
                return new ConflictError($"Статус {command.Status} нельзя выставить вручную");
        }
    }
    catch (DomainException e)
    {
        return new ConflictError(e.Message);
    }

    await db.SaveChangesAsync(ct);
    return extractor.Extract(vacancy);
}

// VacanciesController.cs — только вызов и перевод результата в HTTP
[HttpPost("vacancies/{id}/status")]
public async Task<IActionResult> SetStatus(
    string id,
    [FromBody] SetVacancyStatusRequest request,
    [FromServices] SetVacancyStatusCommandHandler handler,
    CancellationToken ct
)
{
    var command = new SetVacancyStatusCommand { VacancyId = id, Status = request.Status, Reason = request.Reason, Resume = request.Resume };
    return FromResult((await handler.Handle(command, ct)).Value);
}
```

Правило: **команды** меняют состояние через методы агрегатов; **запросы** читают `AsNoTracking` и проецируют в DTO, агрегаты не трогают.

## Принятые решения

| Вопрос | Решение |
|---|---|
| Проекты | 4 проекта: Domain, Application, Infrastructure, Api |
| Результаты | `OneOf` + `OneOf.SourceGenerator`: на каждый сценарий именованный тип `[GenerateOneOf] partial class XxxResult : OneOfBase<...>` рядом с командой |
| Регистрация обработчиков | `Scrutor`: сканирование Application по `ICommandHandler<,>`, `IQueryHandler<,>`, `IExtractor<,>` |
| HTTP | MVC-контроллеры с XML-доками; общий `ApiController.FromResult` переводит результат в ответ |
| OpenAPI UI | `Microsoft.AspNetCore.OpenApi` + `Scalar` на `/scalar` |
| Схема БД | не менялась (проверено `has-pending-model-changes`): `Salary` собирается из прежних колонок |
| API для расширения | маршруты и формат ответов сохранены; тело ошибок — `{ "message": "..." }` |

### Переезд на union-типы .NET 11

Результат сценария объявлен одной строкой рядом с командой, а контроллер разбирает его только через `.Value` и сопоставление с образцом в `ApiController.FromResult` — без `Match`/`Switch` из OneOf. Обработчики возвращают варианты через неявное преобразование (`return new NotFoundError(...)`), которое есть и у union-типов. Переезд:

```csharp
// сейчас
[GenerateOneOf]
public partial class SetVacancyStatusResult : OneOfBase<VacancyDto, NotFoundError, ConflictError>;

// .NET 11
public union SetVacancyStatusResult(VacancyDto, NotFoundError, ConflictError);
```

Обработчики и контроллеры при этом не меняются; если в итоговом дизайне языка у union не будет свойства `Value`, правка одна — в `FromResult`. Варианты — только конкретные типы (генератор OneOf не допускает интерфейсы), поэтому списки — `List<T>`.

## Последствия

- Сценарий ищется по имени файла; новая функция — новая пара команда/обработчик, без правки `Program.cs`.
- Домен тестируется без БД; обработчики — на SQLite в памяти или Testcontainers (#12).
- Больше файлов — цена читаемости; для проекта такого размера приемлемо.

## Что сделано

Команды: `ImportVacancies`, `SetVacancyStatus`, `CollectVacancies`, `SaveSearch`, `DeleteSearch`, `SaveResume`. Запросы: `GetQueue`, `GetVacancies`, `GetStats`, `GetSearches`, `GetResumes`, `GetApplicationsReport`. Фоновый сбор вызывает `CollectVacanciesCommandHandler`. Тесты: 18 xunit (домен, правила, источник hh.ru). Проверено в Docker: все эндпоинты, сбор при старте, OpenAPI и Scalar.
