# Backend (server/)

ASP.NET Core 10 + PostgreSQL. Собирает вакансии, фильтрует/оценивает их, выбирает резюме и отдаёт клиенту готовую очередь.

```
server/
  src/HhAutoApplier.Core            домен и правила (без зависимостей): Vacancy, SearchProfile, FilterRules,
                                    VacancyEvaluator, ResumePicker, StackExtractor
  src/HhAutoApplier.Infrastructure  EF Core (Npgsql); Sources/ — источники вакансий (IVacancySource, Hh/HhWebSource),
                                    Collecting/ — VacancyCollector и фоновый сбор
  src/HhAutoApplier.Api             minimal API, X-Api-Key
  tests/HhAutoApplier.Tests         xunit (правила, стек, выбор резюме, разбор страниц hh.ru)
  docker-compose.yml, Dockerfile    postgres:17 + api
```

## Запуск

```powershell
cd server
copy .env.example .env        # задайте POSTGRES_PASSWORD, API_KEY
docker compose up -d --build  # api: http://127.0.0.1:8080, postgres: 127.0.0.1:${DB_PORT:-5433}
```

Миграции применяются при старте. Резюме «.NET» и «PHP» создаются при первом запуске, правятся через `PUT /api/resumes`.
Локально без Docker: поднимите Postgres и `dotnet run --project src/HhAutoApplier.Api`; тесты: `dotnet test`.

## Источники вакансий

Сборщик работает автономно внутри backend: фоновая задача раз в `Collector:IntervalMinutes` (и сразу при старте) проходит все включённые поиски по всем источникам. Вручную — `POST /api/collect`.

**Источник — плагин** `IVacancySource` (`Sources/`): `SearchAsync` возвращает карточки с выдачи (дёшево), `LoadDetailsAsync` дополняет новую вакансию описанием и навыками. Новый агрегатор — своя реализация и регистрация в `DependencyInjection`.

**hh.ru** (`HhWebSource`, `Hh:Enabled`): публичные страницы поиска и вакансий без токена и аккаунта. Данные берутся из JSON `HH-Lux-InitialState`; разбор — только в `HhPageParser`, при смене вёрстки hh.ru правится он и тесты `HhSourceTests`. Поля поиска (`query`, `area`, `extraParams`) — параметры страницы `hh.ru/search/vacancy`.

Бережность: случайная пауза `Collector:DelayMinMs`–`DelayMaxMs` (2–5 с) между запросами, не больше `Collector:MaxPages` страниц, детали только для новых вакансий, остановка на первой странице без новых. Капча, 403 или 429 — `SourceBlockedException`: сбор останавливается до следующего запуска, в лог — предупреждение, `POST /api/collect` отвечает 502. Это серая зона условий hh.ru: аккаунт не используется, но частые запросы с одного IP могут включить антибот.

Запасной путь — **импорт** `POST /api/import` (`{search, vacancies[]}`): вакансии от любого клиента, например расширения.

## API (заголовок `X-Api-Key`, если задан `Api:Key`)

| Метод | Путь | Назначение |
|---|---|---|
| GET | `/health` | проверка |
| GET | `/api/queue?search=&resume=&limit=` | очередь на отклик (лучшие сверху) |
| GET | `/api/vacancies?status=&search=&skip=&take=` | список |
| POST | `/api/vacancies/{id}/status` | `{status, reason?, resume?}`: Applied, Skipped, Failed... |
| GET/PUT/DELETE | `/api/searches` | сохранённые поиски с правилами (`FilterRules`) |
| GET/PUT | `/api/resumes` | профили резюме (`match`, `keywords`) |
| POST | `/api/import` | `{search, vacancies[]}` |
| POST | `/api/collect` | запустить сбор сейчас (502, если источник заблокировал) |
| GET | `/api/stats` | счётчики по статусам |
| GET | `/api/report?format=csv` | отчёт по откликам |

Пример поиска: `PUT /api/searches` с `{"name":"dotnet","query":"C# .NET","area":"113","extraParams":"work_format=REMOTE","rules":{"excludeKeywords":["junior"],"minSalaryRub":200000}}`.

## Дальше

Режим «из очереди» в расширении, LLM, другие агрегаторы, пользователи и Telegram-бот: см. `ROADMAP.md`.
