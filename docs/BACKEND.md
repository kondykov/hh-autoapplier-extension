# Backend (server/)

ASP.NET Core 10 + PostgreSQL. Собирает вакансии, фильтрует/оценивает их, выбирает резюме и отдаёт клиенту готовую очередь.

```
server/
  src/HhAutoApplier.Core            домен и правила (без зависимостей): Vacancy, SearchProfile, FilterRules,
                                    VacancyEvaluator, ResumePicker, StackExtractor
  src/HhAutoApplier.Infrastructure  EF Core (Npgsql); клиент hh API и VacancyCollector — устаревшие, будут удалены
  src/HhAutoApplier.Api             minimal API, X-Api-Key
  tests/HhAutoApplier.Tests         xunit (правила, стек, выбор резюме)
  docker-compose.yml, Dockerfile    postgres:17 + api
```

## Запуск

```powershell
cd server
copy .env.example .env        # задайте POSTGRES_PASSWORD, API_KEY, HH_USER_AGENT
docker compose up -d --build  # api: http://127.0.0.1:8080, postgres: 127.0.0.1:${DB_PORT:-5433}
```

Миграции применяются при старте. Резюме «.NET» и «PHP» создаются при первом запуске, правятся через `PUT /api/resumes`.
Локально без Docker: поднимите Postgres и `dotnet run --project src/HhAutoApplier.Api`; тесты: `dotnet test`.

## Источники вакансий

Основной источник — **импорт от расширения** `POST /api/import`: расширение собирает карточки на выдаче hh.ru в браузере пользователя и присылает их на сервер. Токены hh.ru серверу не нужны.

Устаревший источник — **API hh.ru** (`VacancyCollector`, раз в `CollectIntervalMinutes`). Без токена приложения `api.hh.ru` отвечает 403, токена нет, поэтому сборщик только пишет в лог предупреждение (`HhAuthException`), а `POST /api/collect` отвечает 502. Будет удалён (см. `ROADMAP.md`, направление A)
   (User-Agent с example.com блокируется).

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
| POST | `/api/collect` | сбор через API hh.ru (устаревший, без токена — 502) |
| GET | `/api/stats` | счётчики по статусам |
| GET | `/api/report?format=csv` | отчёт по откликам |

Пример поиска: `PUT /api/searches` с `{"name":"dotnet","query":"C# .NET","area":"113","extraParams":"schedule=remote","rules":{"excludeKeywords":["junior"],"minSalaryRub":200000}}`.

## Дальше

Сбор из расширения, режим «из очереди», LLM, пользователи и Telegram-бот: см. `ROADMAP.md`.
