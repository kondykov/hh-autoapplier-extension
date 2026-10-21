# Архитектура и план разделения (SOLID)

Исходник: `legacy/hh-autoapply.user.js` (~2000 строк, один IIFE с глобальным `STATE`, UI, DOM-логикой и GM_*-хранилищем).

## Формат

Chrome MV3 расширение: TypeScript-модули, сборка esbuild в один `dist/content.js` (content scripts не поддерживают ES-модули напрямую, поэтому бандл). Из GM_API используются только `GM_getValue/GM_setValue/GM_addStyle`, поэтому перенос прямой: хранилище заменяется на `chrome.storage.local` (асинхронное), стили на CSS-файл/`<style>`.

## Сделано (проверено тестами, typecheck и сборкой)

| Модуль | Откуда в legacy | Ответственность |
|---|---|---|
| `core/selectors.ts` | `SELECTORS` (75-137) | селекторы и текстовые маркеры hh.ru |
| `core/delays.ts` | `DELAYS`, `wait` | задержки |
| `core/config.ts` | `CONFIG` (27-55) | настройки; личные данные из кода убраны |
| `core/daily-limit.ts` | 184-215 | дневной лимит, зависит от `KeyValueStorage` |
| `infra/storage.ts` | `GM_getValue/GM_setValue` | интерфейс + Chrome/Memory реализации |
| `dom/query.ts` | `qs/qsa/firstMatch/isAlreadyResponded` | DOM-утилиты |
| `dom/cover-letter.ts` | `fillTextarea`, `ensureCoverLetterFieldVisible` | работа с письмом |
| `content/main.ts` | `init` (1819) | composition root |
| `core/state.ts` | `STATE`, outline/unclear | модель и репозиторий состояния |
| `infra/logger.ts` | `log` | Logger + sinks (console, panel) |
| `apply/outcome-detector.ts`, `apply/submit.ts`, `apply/pipeline.ts` | 351-650 | пайплайн отклика |
| `search/vacancy-collector.ts` | `collectVacancies` | сбор карточек |
| `ui/panel.ts`, `ui/modal.ts`, `ui/styles.ts` | 658-972 | оверлей |
| `core/controller.ts` | `executeNextStep`, auto | оркестратор (Next/Авто) |

## План (что ещё вынести)

| Модуль | Legacy (строки) | SOLID-принцип |
|---|---|---|
| `core/state.ts` + `infra/state-repository.ts` | `STATE`, `saveState` (139-182) | SRP: модель отдельно от персистентности |
| `infra/logger.ts` (`Logger` интерфейс, console + panel sinks) | `log`, `updateLogPanel` | DIP/OCP: новые приёмники без правки кода |
| `apply/outcome-detector.ts` | `detectApplyOutcome`, `waitForApplyOutcome`, `isOutlineDetected` (351-460) | SRP: только распознавание исхода |
| `apply/submit.ts` | `findSubmitButton`, `attachLetterAndSubmit`, `waitForSubmitResult` (373-555) | SRP |
| `apply/pipeline.ts` | `runUnifiedApply`, `handleOutlineOutcome`, `finishVacancy` (555-650) | OCP: исходы как стратегии (`OutcomeHandler[]`) |
| `pages/page-detector.ts` + `pages/{search,vacancy,response}-step.ts` | `detectPageType`, `handle*Step` (650, 1162-1450) | OCP/LSP: общий интерфейс `PageStep`, выбор по типу страницы |
| `search/vacancy-collector.ts` | `collectVacancies` (978-1085) | SRP |
| `questions/collector.ts` | `collectAndStoreQuestions`, `downloadCollectedQuestions` (1301-1389) | SRP; позже ответы по профилю |
| `outline/outline-store.ts` | `addToOutline`, `clearOutlineLinks` (1189-1230, 1522) | SRP |
| `rejects/auto-reject.ts` | `executeRejectNext` (1651-1815) | SRP: отдельная функция от откликов |
| `ui/panel.ts`, `ui/modal.ts`, `ui/drag.ts`, `ui/styles.css` | 658-972, 1546-1648 | SRP/ISP: UI общается с ядром через узкий интерфейс `Controller` |
| `core/orchestrator.ts` | `executeNextStep`, auto mode (1116-1160, 1451-1518) | DIP: зависит от интерфейсов страниц и хранилища |

Порядок: state/logger → outcome-detector + submit + pipeline (с тестами на HTML-фикстурах) → page steps → UI → rejects. Каждый шаг сопровождается vitest-тестами на сохранённых фрагментах HTML hh.ru.

## Замечания по legacy

- Личные данные (имя, e-mail, Telegram) были прямо в коде письма, URL поиска с `search_session_id` тоже; теперь это настройки.
- `hh-autoresponder.user.js` дублирует часть логики (`log`, UI, drag-drop, state); при переносе объединяется с основным кодом.
- Классы вроде `.magritte-grid-column_m-4___-vMK7_3-0-3` хрупкие и меняются при релизах hh.ru; их нужно либо заменить на `data-qa`, либо изолировать в `selectors.ts`.
- Tampermonkey-bridge файлы с `@require file:///` в расширении не нужны (остались в `legacy/`).

## Запуск

```bash
npm install
npm run typecheck && npm test
npm run build     # dist/ -> chrome://extensions -> Load unpacked
```


## Статус портирования (v0.5)

Перенесено: персистентный авто-режим и возобновление после редиректа (`core/controller.ts`, `core/state.ts`),
политика пустой выдачи (`core/retry-policy.ts`), сбор и скачивание вопросов (`questions/`),
автоотказ (`rejects/matcher.ts`, `rejects/runner.ts`), страницы отклика/вакансии (`pages/vacancy-info.ts`, `ApplyPipeline.applyOnPage`),
`LetterProvider` (`letters/`) со статическим шаблоном, редактор письма и сохранённые поиски в панели.

Хранение: письмо — `AppConfig.coverLetterTemplate`, сохранённые поиски — `AppConfig.presets` (`chrome.storage.local`, ключ `hh_autoapply_config`).

Осталось: автозаполнение анкет из JSON с ответами (legacy `hh-autoresponder.user.js`), `LlmLetterProvider` (письмо по тексту вакансии + шаблон),
выделение `PageStep` как отдельных классов, Telegram-уведомления (режим без присмотра).
