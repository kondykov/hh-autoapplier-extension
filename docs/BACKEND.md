# Backend (server/)

ASP.NET Core 10 + PostgreSQL. Собирает вакансии, фильтрует/оценивает их, выбирает резюме и отдаёт клиенту готовую очередь.

```
server/
  src/HhAutoApplier.Core            домен и правила (без зависимостей): Vacancy, SearchProfile, FilterRules,
                                    VacancyEvaluator, ResumePicker, StackExtractor
  src/HhAutoApplier.Infrastructure  EF Core (Npgsql), клиент hh API, VacancyCollector + фоновый сервис
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

1. **API hh.ru** (`VacancyCollector`, раз в `CollectIntervalMinutes`). Сейчас `api.hh.ru` отвечает 403 без токена приложения:
   зарегистрируйте приложение на https://dev.hh.ru, укажите `HH_ACCESS_TOKEN` и `HH_USER_AGENT` с реальным контактом
   (User-Agent с example.com блокируется).
2. **Импорт от клиента** `POST /api/import` — расширение или Playwright присылает собранные карточки. Работает без токена.

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
| POST | `/api/collect` | запустить сбор сейчас |
| GET | `/api/stats` | счётчики по статусам |
| GET | `/api/report?format=csv` | отчёт по откликам |

Пример поиска: `PUT /api/searches` с `{"name":"dotnet","query":"C# .NET","area":"113","extraParams":"schedule=remote","rules":{"excludeKeywords":["junior"],"minSalaryRub":200000}}`.

## Дальше

Режим «брать вакансии из очереди» в расширении/воркере, Telegram-бот и мультиаккаунтность: см. `MULTI_ACCOUNT_PLAN.md`.
