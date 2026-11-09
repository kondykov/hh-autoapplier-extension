---
name: task-status
description: Перевод задачи по полю Status в GitHub Project #5 (Backlog/Todo/In Progress/Waiting/Done), закрытие issue с комментарием и датами, проверка CI по коммиту. Использовать при взятии задачи, после push и при закрытии.
---

# Статус задачи в Project #5

Правила статусов — в `AGENTS.md` (Git Workflow). Здесь — как их выполнять.

## gh

`gh` — сборка из Copilot desktop, не в PATH: найти `Get-ChildItem $env:LOCALAPPDATA -Filter gh.exe -Recurse` и звать по полному пути (удобнее из PowerShell). Репозиторий `kondykov/hh-autoapplier-extension`, проект — `--owner kondykov` номер `5`.

## Шаги

1. **Item и поля.** `gh project item-list 5 --owner kondykov --format json -L 200` → id элемента по номеру issue; `gh project field-list 5 --owner kondykov --format json` → id поля Status и id вариантов. Id вариантов брать свежими каждый раз.
2. **Смена статуса.** `gh project item-edit --project-id <PVT_...> --id <item> --field-id <status> --single-select-option-id <option>`. После — перечитать item: часть вызовов молча не срабатывает.
3. **CI.** После push: `gh run list -L 5 --json headSha,status,conclusion,databaseId` и брать только запуск с `headSha` == запушенный коммит. Нет запуска за ~3 минуты — пустой коммит. Красный — чинить сразу.
4. **Waiting.** Код запушен, CI зелёный, но ждёт живой проверки или другой задачи: issue открыт, комментарий — коммит, что проверено, чего ждёт.
5. **Done.** `gh issue close <N> --comment "..."` (коммит, что проверено и что нет), затем даты «Начало»/«Срок» через `item-edit --date`.

## Нельзя

- Менять варианты поля Status (`updateProjectV2Field` с `singleSelectOptions`): стирает статусы всех задач.
- Трогать другие проекты владельца (Roadmap #1, OrderFlow).
- Удалять issues, делать PR и merge в `master` без просьбы.
