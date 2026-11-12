---
name: task-status
description: Перевод задачи по полю Status в GitHub Project #5 (Backlog/Todo/In Progress/Waiting/Done), закрытие issue с комментарием и датами, проверка CI по коммиту. Использовать при взятии задачи, после push и при закрытии.
---

# Статус задачи в Project #5

Правила статусов — в `AGENTS.md` (Git Workflow). Здесь — как их выполнять.

## Инструмент

`node scripts/gh-task.mjs` — обёртка над `gh` (системный GitHub CLI в PATH, вход `kondykov` со scope `project`). Сам находит элемент проекта, берёт свежие id поля и вариантов, добавляет issue в проект, если его там нет, и перечитывает результат с повтором: часть изменений GitHub молча не применяет. Справка — без аргументов.

```powershell
node scripts/gh-task.mjs show 86                       # issue и его поля в проекте
node scripts/gh-task.mjs board                         # Todo / In Progress / Waiting
node scripts/gh-task.mjs status 86 "In Progress"       # взял задачу — до первой правки
node scripts/gh-task.mjs ci --wait                     # CI по HEAD: ждёт, при падении — хвост лога
node scripts/gh-task.mjs status 86 Waiting --comment "..."
node scripts/gh-task.mjs done 86 --comment-file -      # текст из stdin; закрыть, Done, даты
```

Длинный комментарий с кавычками — через `--comment-file путь` или stdin, а не `--comment`.

## Шаги

1. **Взял задачу:** `status N "In Progress"`.
2. **CI после push:** `ci --wait` (по умолчанию HEAD; можно sha). Учитывается только запуск с `headSha` этого коммита. Код 2 «запусков нет» спустя ~3 минуты — пустой коммит. Код 1 — красный, чинить сразу.
3. **Waiting:** код запушен, CI зелёный, но ждёт живой проверки или другой задачи — `status N Waiting --comment` (коммит, что проверено, чего ждёт). Issue открыт.
4. **Done:** `done N --comment` — комментарий (коммит, что проверено и что нет), закрытие, Status Done, «Начало» (уже стоящее, иначе дата первого коммита с `#N`, или `--start`) и «Срок» (сегодня или `--end`).

Проверять доработки скрипта — на временном проекте и не закреплённом репозитории: `--project N --repo owner/name`, не на Project #5.

## Нельзя

- Менять варианты поля Status в Project #5 (`updateProjectV2Field` с `singleSelectOptions`): стирает статусы всех задач.
- Трогать другие проекты владельца (Roadmap #1, OrderFlow).
- Удалять issues, делать PR и merge в `master` без просьбы.
