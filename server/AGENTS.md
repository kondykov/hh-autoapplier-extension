# server/AGENTS.md — backend (ASP.NET Core 10)

Дополняет корневой `AGENTS.md`; при конфликте главнее этот файл.

## Commands
```powershell
dotnet test
docker compose up -d --build        # db (host 5433) + api (127.0.0.1:8080)
.\..\.tools\dotnet-ef migrations add <Name> -p src/HhAutoApplier.Infrastructure -s src/HhAutoApplier.Api -o Persistence/Migrations
```

## Structure
Core (домен, правила, без EF/HTTP) ← Infrastructure (EF; `HhApiClient` и `VacancyCollector` устарели, будут удалены) ← Api (minimal API, `X-Api-Key`).

## Rules
- Новая логика — в Core + xunit-тест; endpoint тонкий
- Сидирование — `DbSeeder`, не `HasData`
- Только nuget.org (`nuget.config`): в глобальном конфиге владельца недоступный источник
- Сервер не ходит на hh.ru: вакансии приходят от расширения через `POST /api/import`. `api.hh.ru` без токена приложения = 403, токена нет; новую логику на API hh.ru не строить
- Секреты (`API_KEY`, пароль БД) только в `.env`

## Boundaries
- ⚠️ Миграции, смена схемы, новые NuGet-пакеты — спросить
- ❌ Коммит `.env`; удаление миграций

→ Full rules: ../.rules/ ; подробности — ../docs/BACKEND.md
