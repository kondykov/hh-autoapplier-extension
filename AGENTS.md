# AGENTS.md — hh-autoapplier-extension

Личный полуавтоматический инструмент откликов на hh.ru: MV3-расширение (TypeScript) + backend (ASP.NET Core 10, PostgreSQL). Общаться по-русски.

## Commands
```powershell
npm run typecheck                 # tsc --noEmit
npm test                          # vitest (29 тестов)
npm run build                     # dist/content.js (esbuild)
node scripts/dev-browser.mjs      # Chromium с расширением, профиль .profile, CDP :9222
cd server; dotnet test            # xunit (5 тестов)
cd server; docker compose up -d --build   # postgres:17 + api на 127.0.0.1:8080
```
Расширение читается только при старте браузера: после `build` браузер перезапускать (спросить, если у владельца открыта страница).

## Testing
- Чистая логика — тесты в `tests/` (vitest + happy-dom), без живого hh.ru
- Backend — `server/tests/HhAutoApplier.Tests`
- Перед «готово»: typecheck + test + build (или `dotnet test`)
- Живую страницу проверять read-only через CDP; не нажимать «Отправить»

## Project Structure
- `src/content/main.ts` — композиция; `src/core/` — Controller, config, selectors
- `src/{apply,search,filter,resume,report,questions,rejects,letters,captcha,dom,infra,ui}` — модули
- `server/src/HhAutoApplier.{Core,Infrastructure,Api}` — домен, EF/клиент hh, minimal API
- `docs/` — для людей

## Code Style
```ts
// ✅ селектор из реестра src/core/selectors.ts
qs<HTMLElement>(SELECTORS.responseModalClose)?.click();
// ❌ document.querySelector('[data-qa="..."]') в логике
```
```csharp
// ✅ правила в Core, endpoint тонкий
// ❌ бизнес-логика в Program.cs; HasData со списками (сидировать через DbSeeder)
```
Новое поле конфига — и в дефолты, и в слияние `loadConfig`.

## Git Workflow
- Коммит в текущую ветку; в конце сообщения: `Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>`
- `git push` и PR — только по просьбе

## Boundaries
- ✅ Править `src/`, `server/`, `tests/`, `docs/`; запускать тесты/сборку; локальный docker compose
- ⚠️ Спросить: новые зависимости, миграции EF, перезапуск браузера, запуск Docker Desktop, смена задержек/лимитов
- ❌ Реальные отклики без согласия; коммит `letter.local.txt`, `server/.env`, `.profile/`, токенов; обход/автоматическое решение капчи

→ Full rules: .rules/ (начать с `.rules/_index.md`, статус — `.rules/context/task.md`)
