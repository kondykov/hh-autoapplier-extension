# CLAUDE.md

Руководство для AI-агентов и разработчиков. Язык проекта: комментарии, UI и документация на русском, идентификаторы на английском.

## Что это

Личный полуавтоматический инструмент отклика на вакансии hh.ru. Исходник — пользовательские скрипты коллеги-QA (`legacy/`),
переписанные в модульное расширение Chrome (MV3, TypeScript) плюс backend (ASP.NET Core + PostgreSQL), который собирает
и фильтрует вакансии и отдаёт клиенту готовую очередь. Владелец — .NET-разработчик (бывший PHP), два резюме на hh.ru: PHP и .NET.

## Расположение

- Рабочая копия (worktree Copilot App): `C:\Users\kondy\.copilot\repos\copilot-worktrees\hh-autoapplier-extension\kondykov-supreme-spoon`
- Ветка `kondykov-new-hh-autoapplier-repo`; репозиторий `kondykov/hh-autoapplier-extension` (ничего не запушено, PR нет)
- Отдельный репозиторий `kondykov/hh-autoapplier` (private) — заготовка под Playwright-воркер, пока пустая

## Карта проекта

```
src/                    расширение (TypeScript), точка входа src/content/main.ts (composition root)
  core/                 controller (оркестратор), config, state, selectors (ВСЕ селекторы hh.ru тут), delays, retry-policy, daily-limit
  apply/                pipeline, submit, outcome-detector
  search/ pages/        сбор карточек на выдаче, данные со страницы вакансии
  filter/ resume/       правила и скоринг; выбор резюме по стеку
  report/ questions/ rejects/ letters/ captcha/   отчёт CSV, анкеты работодателей, автоотказы, письма, автопауза при капче
  dom/ infra/ ui/       хелперы DOM, storage/logger, панель-оверлей
tests/                  vitest + happy-dom
server/                 backend: Core (домен, правила) / Infrastructure (EF Core, hh API, сборщик) / Api (minimal API) / Tests
scripts/                build.mjs (esbuild → dist/content.js), dev-browser.mjs (Playwright Chromium + расширение, CDP :9222)
docs/                   ARCHITECTURE.md, BACKEND.md, MULTI_ACCOUNT_PLAN.md
legacy/                 исходные userscripts коллеги (содержат личные данные, не править)
```

## Команды

Расширение (корень):
```
npm run typecheck && npm test && npm run build
node scripts/dev-browser.mjs     # браузер с расширением; профиль .profile (там авторизация hh.ru), CDP http://127.0.0.1:9222
```
Расширение читается при старте браузера: после каждой сборки браузер надо перезапускать.

Backend (`server/`):
```
dotnet test
copy .env.example .env; docker compose up -d --build      # api :8080, postgres 127.0.0.1:${DB_PORT:-5433}
.\..\.tools\dotnet-ef migrations add <Name> -p src/HhAutoApplier.Infrastructure -s src/HhAutoApplier.Api -o Persistence/Migrations
```
Установка dotnet-ef: `dotnet tool install dotnet-ef --tool-path .tools` из корня репозитория (`.tools/` в gitignore).
`server/nuget.config` оставляет только nuget.org (на машине владельца в глобальном конфиге есть недоступный приватный источник).

## Правила и договорённости

- Поставка расширения — один файл `dist/content.js` (esbuild); в исходниках модули по SOLID. Не возвращаться к «god file» в исходниках.
- Селекторы hh.ru — только в `src/core/selectors.ts`. hh.ru меняет вёрстку: при поломке сначала проверять селекторы (через CDP на живой странице).
- Личное письмо: `letter.local.txt` (в gitignore), подставляется при сборке через `define` `__DEFAULT_LETTER__`. Не коммитить личные данные. Плейсхолдеры `{{title}}`, `{{company}}`.
- Сохранённая конфигурация панели (chrome.storage) перекрывает значения по умолчанию из сборки.
- `chrome.storage`-конфиг мерджится поверхностно в `loadConfig`: новые поля конфига нужно добавлять и в дефолты, и в слияние.
- Реальные отклики без явного согласия владельца не отправлять. Зонды и проверки на живой странице делать без отправки.
- Новая логика — с тестами (vitest / xunit); чистые функции держать отдельно от DOM.
- Автоматизация hh.ru нарушает их условия: не наращивать частоту, соблюдать задержки и дневной лимит; капчу решает человек (автопауза), обход не делать.
- Коммиты — на русском или английском, с трейлером `Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>`.

## Windows-особенности для агентов

- Инструмент `create` не перезаписывает существующий файл и не создаёт каталоги: каталог создавать через `New-Item`, файл — удалить и создать заново.
- Многострочные правки через PowerShell: нормализовать CRLF → LF перед `.Replace`. Предупреждения git про LF→CRLF безвредны.
- Docker Desktop может быть не запущен; порт 5432 на машине владельца бывает занят (поэтому БД на 5433).

## Статус (на 2026-10-23)

Готово и проверено (typecheck, 29 тестов vitest, 5 тестов xunit, сборка):
- Расширение: оверлей, сбор вакансий, фильтры и скоринг по пресетам, автоотклик с письмом (универсальное + пресеты), автовыбор резюме PHP/.NET, отчёт CSV, сбор анкет, автоотказы, повторы пустой выдачи, дневной лимит, автопауза при капче.
- Backend: сохранённые поиски, оценка и выбор резюме, очередь, статусы, отчёт, импорт `POST /api/import`, Docker (postgres + api).

Не проверено на живых данных: автовыбор резюме в выпадающем списке hh.ru (селекторы подтверждены, клик не прогонялся), автопауза при капче, сбор через API hh.ru.

Блокеры и ограничения:
- `api.hh.ru` отдаёт 403 без токена приложения (нужна регистрация на dev.hh.ru, `HH_ACCESS_TOKEN`); User-Agent с example.com блокируется.
- Правила фильтрации в расширении работают только на странице выдачи; отфильтрованные не попадают в отчёт.
- В браузерном профиле сохранено старое PHP-письмо — заменить в панели.

## Следующие шаги

1. Токен hh.ru или отправка собранных карточек из расширения в `POST /api/import`.
2. Режим «брать вакансии из очереди backend» в расширении, статусы обратно в `/api/vacancies/{id}/status`.
3. Проверка автовыбора резюме и автопаузы на живой странице.
4. Telegram-бот (уведомления при капче/ошибках, управление) и мультиаккаунтность — `docs/MULTI_ACCOUNT_PLAN.md`.
5. LLM-оценка вакансий и адаптация письма, ручное подтверждение через Telegram (отложено по решению владельца).
6. Автозаполнение анкет работодателей из собранных вопросов (идея из `legacy/hh-autoresponder`).
