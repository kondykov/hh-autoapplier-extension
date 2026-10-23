# Copilot instructions

Личный инструмент откликов на hh.ru: MV3-расширение (TypeScript, esbuild, vitest) + backend (ASP.NET Core 10, EF Core, PostgreSQL). Отвечай по-русски, кратко.

## Commands
- `npm run typecheck`, `npm test`, `npm run build`
- `cd server; dotnet test`

## Rules
- Селекторы hh.ru — только в `src/core/selectors.ts`
- Чистая логика отдельно от DOM, с тестами
- Новое поле конфига — в дефолты и в `loadConfig`
- Backend: логика в Core, endpoint тонкий, сидирование через `DbSeeder`

## Never
- Отправлять реальные отклики без согласия владельца
- Коммитить `letter.local.txt`, `server/.env`, `.profile/`, токены
- Обходить или автоматически решать капчу

Коммиты: трейлер `Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>`.

→ Полные правила: `AGENTS.md`, `.rules/`
