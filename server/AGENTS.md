# server/AGENTS.md — backend (ASP.NET Core 10)

Дополняет корневой `AGENTS.md`; при конфликте главнее этот файл.

## Commands
```powershell
dotnet test                         # нужен Docker: тесты API поднимают postgres:17 в Testcontainers
docker compose up -d --build        # db (host 5433) + api (127.0.0.1:8080)
.\..\.tools\dotnet-ef migrations add <Name> -p src/HhAutoApplier.Infrastructure -s src/HhAutoApplier.Api -o Persistence/Migrations
```

## Structure
DDD + простой CQRS без MediatR (`../docs/adr/0001-backend-ddd-cqrs.md`):
Domain (агрегаты, value objects, доменные сервисы; без зависимостей) ← Application (Commands/Queries + Handlers, Dtos, Extractors, порты `IAppDbContext`/`IVacancySource`) ← Infrastructure (EF, источники `Sources/`, фоновый сбор) ← Api (контроллеры, `Auth/` — cookie Identity и `X-Api-Key` через обработчик аутентификации, OpenAPI + Scalar на `/scalar`).

## Rules
- Новый сценарий: `XxxCommand`/`XxxQuery` (record + DataAnnotations) и `XxxCommandHandler`/`XxxQueryHandler` в `Application/<Commands|Queries>/<Фича>/`; регистрация автоматическая (Scrutor)
- Результат с ошибками — `[GenerateOneOf] partial class XxxResult : OneOfBase<...>` рядом с командой; контроллер разбирает `.Value` через `FromResult`. Не вызывать `Match`/`Switch` OneOf в контроллерах — так переезд на union-типы .NET 11 сводится к замене объявления результата
- Правила и переходы статусов — методы агрегатов (сеттеры закрыты), нарушение — `DomainException`; обработчик переводит её в `ConflictError`/`ValidationError`
- Новая логика — с xunit-тестом (домен — без БД). Новый эндпоинт — тест в `tests/HhAutoApplier.Tests/Api/ApiTests.cs` через `ApiFactory` (настоящий Postgres, подменный `IVacancySource`, на hh.ru не ходит)
- Сидирование — `DbSeeder`, не `HasData`
- Только nuget.org (`nuget.config`): в глобальном конфиге владельца недоступный источник
- Вакансии собирает сервер через источники-плагины `IVacancySource`; hh.ru — публичные страницы без токена (`HhWebSource`), разбор только в `HhPageParser` + тест на фикстуре. `api.hh.ru` не использовать (403 без токена)
- Не ускорять сбор: паузы `Collector:DelayMinMs/MaxMs`, лимит страниц; при капче/403/429 — остановка (`SourceBlockedException`), не обход
- Доступ: без атрибутов эндпоинт только для `Admin` (fallback-политика); открыть — `[Authorize]` (любой вошедший) или `[AllowAnonymous]`. Текущий пользователь — `ICurrentUser` (при `X-Api-Key` id = null). Подробно — `../docs/BACKEND.md`, «Пользователи и вход»
- Секреты (`API_KEY`, `ADMIN_PASSWORD`, пароль БД) только в `.env`

## Boundaries
- ⚠️ Миграции, смена схемы, новые NuGet-пакеты — спросить
- ❌ Коммит `.env`; удаление миграций

→ Full rules: ../.rules/ ; подробности — ../docs/BACKEND.md
