# AGENTS.md — hh-autoapplier

Личный полуавтоматический инструмент откликов на hh.ru: MV3-расширение (TypeScript) + backend (ASP.NET Core 10, PostgreSQL). Общаться по-русски, кратко.

Файл для любых ИИ-агентов. Это единственный источник инструкций: файлы конкретных инструментов (`CLAUDE.md`) только ссылаются сюда, отдельные копии правил не заводить.

## Commands
```powershell
npm run typecheck                 # tsc --noEmit
npm test                          # vitest (104 теста)
npm run build                     # dist/content.js + dist/background.js (esbuild)
node scripts/dev-browser.mjs      # Chromium с расширением, профиль .profile, CDP :9222
node scripts/cdp-queue-dry-run.mjs # пробный проход очереди в запущенном dev-браузере: журнал и снимок формы, без отправки
cd server; dotnet test            # xunit (126 тестов; нужен запущенный Docker — Postgres для тестов API в Testcontainers)
cd server; docker compose up -d --build   # postgres:17 + api на 127.0.0.1:8080
```
Расширение читается только при старте браузера: после `build` браузер перезапускать (спросить, если у владельца открыта страница).

## Testing
- Чистая логика — тесты в `tests/` (vitest + happy-dom), без живого hh.ru; разметка формы отклика hh.ru — `tests/fixtures/response-form.ts` (по живой странице), её же брать для новых тестов конвейера
- Backend — `server/tests/HhAutoApplier.Tests`
- Перед «готово»: typecheck + test + build (или `dotnet test`); то же гоняет CI (`.github/workflows/ci.yml`) на каждый push, плюс проверку, что модель EF совпадает с миграциями
- Живую страницу проверять read-only через CDP; не нажимать «Отправить». Перед открытием вакансии убедиться, что очередь выключена и пробный режим включён — режим очереди переживает перезапуск браузера. Перед закрытием или перезапуском dev-браузера проверить вкладки: открыто что-то кроме стартовой страницы — спросить владельца

## Project Structure
- `src/content/main.ts` — композиция; `src/core/` — Controller (сборка и выбор шага), settings, config, selectors; `src/pages/*-step.ts` — шаги страниц (выдача → сервер, очередь, отказы) на `StepContext`
- `src/{apply,search,resume,report,questions,rejects,letters,captcha,dom,infra,ui}` — модули
- `src/api` — клиент backend и транспорт; `src/background/main.ts` — service worker, через который идут все запросы к backend (со страницы hh.ru Chrome блокирует запросы в локальную сеть)
- `server/src/HhAutoApplier.{Domain,Application,Infrastructure,Api}` — DDD + CQRS: агрегаты, команды/запросы, EF и источники, контроллеры (`docs/adr/0001-backend-ddd-cqrs.md`)
- `docs/` — для людей; план доработок — `docs/ROADMAP.md`

## Code Style
```ts
// ✅ селектор из реестра src/core/selectors.ts
qs<HTMLElement>(SELECTORS.responseModalClose)?.click();
// ❌ document.querySelector('[data-qa="..."]') в логике
```
```csharp
// ✅ сценарий = Command/Query + Handler в Application; правила — в методах агрегатов Domain; контроллер только вызывает обработчик
// ❌ логика в контроллере или Program.cs; публичные сеттеры у агрегатов; HasData (сидировать через DbSeeder)
```
Новое поле конфига — и в дефолты, и в слияние `loadConfig`.

## Git Workflow
- Коммит в текущую ветку; в конце сообщения трейлер `Co-authored-by:` агента, который делал изменения
- Рабочая ветка `develop` (до 2026-10-25 — `kondykov-new-hh-autoapplier-repo`): коммит и `git push` в неё — без отдельного вопроса, после локальных typecheck/test/build (или `dotnet test`)
- После push дождаться CI; красный CI — чинить сразу, это часть задачи
- Задачи — GitHub Issues, карта и порядок — `docs/ROADMAP.md`; в коммите `refs #N`; заводить issues под найденные задачи — можно, с метками
- Статус задачи — поле Status в Project #5 (https://github.com/users/kondykov/projects/5), его ведёт агент:
  - **Backlog** — пул всех задач вне спринта в работе: новые issues кладутся сюда
  - **Todo** — только задачи спринта в работе (если у текущего остались одни Waiting — следующего). Спринт закрылся (все задачи Done) — задачи следующего из Backlog в Todo
  - **Todo** → **In Progress** — в момент, когда берёшь задачу, до первой правки
  - **In Progress** → **Waiting** — код запушен и CI зелёный, но задача ждёт проверки (живой прогон, проверка владельцем) или связанной задачи и в релиз ещё не может. Issue остаётся открытым; в комментарии — коммит, что проверено, чего ждёт
  - → **Done** — всё проверено и ничего не ждёт: issue закрыть с комментарием о коммите и о том, что проверено и что нет; поставить «Начало»/«Срок»
  - Варианты поля Status не менять через API: замена списка вариантов стирает статусы всех задач
- PR, merge в `master` (до 2026-10-25 — `main`), удаление веток и issues — только по просьбе

## Boundaries
- ✅ Править `src/`, `server/`, `tests/`, `docs/`; запускать тесты/сборку; локальный docker compose
- ✅ Без вопроса (разрешение владельца от 2026-10-24): push в рабочую ветку, закрытие сделанных issues, запуск Docker Desktop и dev-браузера, read-only проверки на hh.ru
- ⚠️ Спросить: новые зависимости, миграции EF, смена задержек/лимитов, перезапуск dev-браузера, если у владельца открыта страница
- ❌ Реальные отклики без согласия (режим «из очереди» проверять только в пробном режиме — галочка в панели); коммит `letter.local.txt`, `server/.env`, `.profile/`, токенов; обход/автоматическое решение капчи

→ Full rules: .rules/ (начать с `.rules/_index.md`, статус — `.rules/context/task.md`, план — `docs/ROADMAP.md`)
