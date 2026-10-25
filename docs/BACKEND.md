# Backend (server/)

ASP.NET Core 10 + PostgreSQL. Собирает вакансии, фильтрует/оценивает их, выбирает резюме и отдаёт клиенту готовую очередь.

Архитектура — DDD и простой CQRS без MediatR, решение и примеры: [adr/0001-backend-ddd-cqrs.md](adr/0001-backend-ddd-cqrs.md). Документация API: `http://127.0.0.1:8080/scalar` (без ключа).

```
server/
  src/HhAutoApplier.Domain          агрегаты Vacancy, SearchProfile, ResumeProfile; value objects Salary, FilterRules;
                                    доменные сервисы VacancyEvaluator, ResumePicker, StackExtractor (без зависимостей)
  src/HhAutoApplier.Application     Commands/ и Queries/ по фичам (команда + обработчик), Dtos/, Extractors/,
                                    Errors/, порты Abstractions/ (IAppDbContext, IVacancySource)
  src/HhAutoApplier.Infrastructure  EF Core (Npgsql, Configurations/, миграции, DbSeeder); Sources/Hh — источник hh.ru;
                                    Collecting/ — фоновый сбор по расписанию
  src/HhAutoApplier.Api             контроллеры, X-Api-Key, OpenAPI + Scalar
  tests/HhAutoApplier.Tests         xunit: домен, правила, разбор страниц hh.ru; Api/ — интеграционные тесты
                                    API (WebApplicationFactory + Postgres 17 в Testcontainers, нужен Docker)
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

**Источник — плагин** `IVacancySource` (порт в Application, реализации в `Infrastructure/Sources/`): `SearchAsync` возвращает карточки с выдачи (дёшево), `LoadDetailsAsync` дополняет новую вакансию описанием и навыками. Новый агрегатор — своя реализация и регистрация в `Infrastructure/DependencyInjection`. Сам сбор — `CollectVacanciesCommandHandler`.

**hh.ru** (`HhWebSource`, `Hh:Enabled`): публичные страницы поиска и вакансий без токена и аккаунта. Данные берутся из JSON `HH-Lux-InitialState`; разбор — только в `HhPageParser`, при смене вёрстки hh.ru правится он и тесты `HhSourceTests`. Поля поиска (`query`, `area`, `extraParams`) — параметры страницы `hh.ru/search/vacancy`.

Бережность: случайная пауза `Collector:DelayMinMs`–`DelayMaxMs` (2–5 с) между запросами, не больше `Collector:MaxPages` страниц, детали только для новых вакансий, остановка на первой странице без новых. Капча, 403 или 429 — `SourceBlockedException`: сбор останавливается до следующего запуска, в лог — предупреждение, `POST /api/collect` отвечает 502. Это серая зона условий hh.ru: аккаунт не используется, но частые запросы с одного IP могут включить антибот.

Запасной путь — **импорт** `POST /api/import` (`{search, vacancies[]}`): вакансии от любого клиента, например расширения.

## LLM

Порт `ILlmClient` (Application) и `OpenAiCompatibleClient` (Infrastructure/Llm) — chat/completions любого OpenAI-совместимого провайдера; по умолчанию Groq. Настройки `Llm:*` (в `.env`: `LLM_API_KEY`, `LLM_BASE_URL`, `LLM_MODEL`, `LLM_DAILY_LIMIT`); без ключа LLM выключена. Повторы при 429 и 5xx (пауза — `Retry-After` или 1, 2, 4 с), таймаут, дневной лимит запросов (`LlmBudget`: счёт за сутки UTC, после перезапуска продолжается по журналу). Ключ Groq: https://console.groq.com/keys.

Каждый запрос пишется в журнал `LlmCalls`: назначение, вакансия, модель, промпт, ответ или ошибка, токены.

**Оценка вакансий (#22).** Если LLM настроена, вакансия, прошедшая правила, получает статус `Pending` и в очередь расширения не попадает. После каждого фонового сбора (и по `POST /api/llm/evaluate`) `EvaluatePendingVacanciesCommandHandler` отправляет LLM резюме (`Summary` профиля, иначе ключевые слова), пожелания из правил поиска и вакансию; ответ — JSON `{verdict, score, resume, reason}`:
- `fit` — в очередь (`Queued`), `Score` = оценка LLM, резюме — выбранное LLM;
- `doubtful` — в очередь с причиной «LLM сомневается: …» (ручная проверка — #25);
- `reject` — `Skipped` с причиной «LLM: …».

Неразобранный ответ считается сомнительным. При исчерпании лимита или недоступности провайдера оценка останавливается, остальные ждут следующего запуска. Без LLM вакансии идут в очередь сразу, как раньше. Поле `Summary` резюме заполняется через `PUT /api/resumes` — кратко о себе: опыт, стек, что интересно и что нет.

**Письма под вакансию (#23).** После оценки (и по `POST /api/llm/letters`) `WriteCoverLettersCommandHandler` пишет письма для вакансий `Queued` без письма, лучшие по баллу первыми. LLM получает резюме, выбранное для вакансии (`Summary` или ключевые слова), его шаблон письма `LetterTemplate` и вакансию. С шаблоном LLM сохраняет приветствие, стиль, подпись и контакты и меняет середину под вакансию; без шаблона пишет сама. Ответ — текст письма; пустой, слишком короткий или JSON не сохраняется, вакансия ждёт следующего запуска. Промпт строгий: имя, подпись и контакты — только из шаблона (без шаблона — письмо без них), цифры, стаж, навыки и места работы — только из резюме и шаблона, ключевые слова без `Summary` не выдаются за опыт (живая проверка 2026-10-25: без `Summary` LLM выдумывала имя и стаж). Письмо (`coverLetter`) уходит в расширение вместе с `/api/queue` и в режиме «из очереди» важнее шаблонов расширения. Без LLM писем нет. Переписать письма очереди после правки резюме, шаблона или промпта: `POST /api/llm/letters/reset` (все или `{vacancyIds}`), затем `POST /api/llm/letters` или фоновый запуск.

**Ручная проверка (#25).** LLM сомневается (`doubtful`) — вакансия получает статус `Review` и в очередь расширения не идёт; правило поиска `skipReview: true` — сразу в очередь, как раньше. Письма пишутся и для `Review`, чтобы их было видно на проверке. Страница `/review.html` (wwwroot, отдаётся без ключа — ключ она спрашивает сама и хранит в `localStorage` браузера): список `GET /api/vacancies?status=Review`, у карточки — описание, причина LLM, анкета, письмо в поле для правки; «Одобрить» → `POST /api/vacancies/{id}/review` `{approve: true, coverLetter?}` — в очередь, поправленное письмо сохраняется с моделью `manual`; «Отклонить» → `{approve: false, reason?}` — `Skipped` «отклонено вручную: …». Анкеты на проверку не идут (решение владельца 2026-10-25). Telegram-бот (#28) подключится к тому же эндпоинту.

**Анкеты работодателей (#24).** Вопросы анкеты видны только со входом на hh.ru: без входа страница отклика ведёт на регистрацию, а страница вакансии знает только `test.hasTests` (проверено 2026-10-25). Поэтому их присылает расширение: `POST /api/vacancies/{id}/questionnaire` с `{questions: [{question, type: Radio|Checkbox|Text|Unknown, options}]}` → статус `Questionnaire`, прежние ответы сбрасываются. После оценки (и по `POST /api/llm/answers`) `AnswerQuestionnairesCommandHandler` отправляет LLM резюме вакансии, пожелания из правил поиска (зарплата, формат), вакансию и пронумерованные вопросы; ответ — JSON `{answers: [...]}` по порядку вопросов. Агрегат проверяет ответы: вариант — только из анкеты (без учёта регистра, сохраняется в написании анкеты), radio — ровно один, checkbox — хотя бы один, текст — непустой. Всё сошлось — вакансия в очереди с ответами (`questions[].answer`, причина «анкета: ответы LLM»); нет — `Skipped` с причиной, догадки работодателю не уходят. Ответы идут в очередь без ручной проверки — решение владельца 2026-10-25. Анкета хранится в `Vacancies.Questions` (jsonb).

## API (заголовок `X-Api-Key`, если задан `Api:Key`)

| Метод | Путь | Назначение |
|---|---|---|
| GET | `/health` | проверка |
| GET | `/review.html` | страница ручной проверки (без ключа) |
| GET | `/api/queue?search=&resume=&limit=` | очередь на отклик (лучшие сверху) |
| GET | `/api/vacancies?status=&search=&skip=&take=` | список |
| POST | `/api/vacancies/{id}/status` | `{status, reason?, resume?}`: Applied, Skipped, Failed... |
| GET/PUT/DELETE | `/api/searches` | сохранённые поиски с правилами (`FilterRules`) |
| GET/PUT | `/api/resumes` | профили резюме (`match`, `keywords`; для LLM — `summary` и шаблон письма `letterTemplate`) |
| POST | `/api/import` | `{search, vacancies[]}` |
| POST | `/api/collect` | запустить сбор сейчас (502, если источник заблокировал) |
| POST | `/api/llm/check` | проверить ключ и модель LLM (503 — не настроена, лимит, провайдер не отвечает) |
| POST | `/api/llm/evaluate` | `{limit}`: оценить LLM вакансии `Pending` (503 — LLM не настроена) |
| POST | `/api/vacancies/{id}/review` | `{approve, coverLetter?, reason?}`: ручная проверка вакансии `Review` |
| POST | `/api/vacancies/{id}/questionnaire` | `{questions[]}`: анкета от расширения → `Questionnaire` |
| POST | `/api/llm/answers` | `{limit}`: ответить LLM на анкеты `Questionnaire` (503 — LLM не настроена) |
| POST | `/api/llm/letters` | `{limit}`: написать LLM письма для очереди без письма (503 — LLM не настроена) |
| POST | `/api/llm/letters/reset` | `{vacancyIds?}`: сбросить письма очереди, чтобы написать заново (без LLM) |
| GET | `/api/stats` | счётчики по статусам |
| GET | `/api/report?format=csv` | отчёт по откликам |

Пример поиска: `PUT /api/searches` с `{"name":"dotnet","query":"C# .NET","area":"113","extraParams":"work_format=REMOTE","rules":{"excludeKeywords":["junior"],"minSalaryRub":200000}}`.

## Дальше

Режим «из очереди» в расширении, LLM, другие агрегаторы, пользователи и Telegram-бот: см. `ROADMAP.md`.
