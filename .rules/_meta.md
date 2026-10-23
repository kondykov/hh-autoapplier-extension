# Meta: версия и журнал правил

## Version: 1.0 — 2026-10-23
## Generator: DEMIURGOS v22
## Sizing: Знания=M × Инструменты=любые агенты (через `AGENTS.md`) × Расширения=0

## Files
| Файл | Назначение |
|---|---|
| `_index.md` | роль, иерархия, права доступа |
| `core/constraints.md` | стиль, защита, безопасность отправки |
| `core/architecture.md` | структура и решения с причинами |
| `context/task.md` | статус и следующие шаги (динамический) |
| `generation/output.md` | контракт ответа |
| `generation/patterns.md` | паттерны и антипаттерны |
| `quality/eval.md` | чеклисты и проверочные запросы |

Точка входа для агентов: `AGENTS.md` (корень) и `server/AGENTS.md`; их читают Copilot, Codex, Cursor и другие. `CLAUDE.md` — только импорт `AGENTS.md` для Claude Code. Отдельные файлы под инструменты не заводить.
Не создавалось: `memory.md`/`safety.md`/`roles.md` (L+), `debug.md` (по запросу).

## Журнал изменений
- 2026-10-23 — первая версия; перенесено из ad-hoc `CLAUDE.md`
- 2026-10-23 — `docs/ARCHITECTURE.md` переписан по текущему коду, README ссылается на `AGENTS.md` вместо `CLAUDE.md`
- 2026-10-23 — удалён `legacy/`, правило «не править legacy» снято; логика автозаполнения анкет описана в `docs/ARCHITECTURE.md`
- 2026-10-23 — инструкции сделаны общими для всех агентов: удалён `.github/copilot-instructions.md`, трейлер коммита без привязки к агенту, `CLAUDE.md` импортирует `AGENTS.md`; добавлен `docs/ROADMAP.md`
- 2026-10-23 — ROADMAP: целевая схема (backend готовит отклики, RBAC, LLM, расширение — исполнитель), архитектура, доступ из WAN, задачи в GitHub Issues
- 2026-10-23 — схема «расширение собирает → backend оценивает → расширение откликается»; без API hh.ru и мультиаккаунта; удалён `docs/MULTI_ACCOUNT_PLAN.md`
- 2026-10-23 — сбор вакансий снова на сервере: автономный, с публичных страниц hh.ru, источники-плагины `IVacancySource`
- 2026-10-23 — backend переведён на DDD + простой CQRS (Domain/Application/Infrastructure/Api), правила под новую структуру
