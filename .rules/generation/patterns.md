# Patterns: рабочие паттерны и антипаттерны

## Новый селектор hh.ru
1. Проверить на живой странице через CDP (`connectOverCDP`, только чтение)
2. Добавить в `SELECTORS`; использовать через `qs`/`qsa`
3. Если страница могла быть в навигации — повторить зонд

Вариант: если у элемента нет стабильного `data-qa`, искать по роли и тексту (`[role="option"]` + `textContent`), как в выборе резюме.

## Новая функция в расширении
```ts
// ✅ чистая логика в модуле + тест рядом по смыслу в tests/
export const isCaptchaVisible = (root: ParentNode = document) => root.querySelector(SEL) !== null;
// ✅ тест на happy-dom, без реального браузера
// ❌ логика, которую можно проверить только руками на hh.ru
```

## Новое поле конфига
Тип → дефолт → слияние в `loadConfig` → сеттер в `Controller` → поле в панели (если нужно) → тест.

## Новый endpoint backend
Правило → метод агрегата в `Domain` + xunit-тест; сценарий → `Application/<Commands|Queries>/<Фича>/XxxCommand.cs` + `XxxCommandHandler.cs` (результат с ошибками — `[GenerateOneOf] partial class XxxResult`); маршрут → метод контроллера в `Api/Controllers` с `[FromServices]` обработчиком и XML-комментарием. Схема БД изменилась → миграция:
```
dotnet ef migrations add <Name> -p src/HhAutoApplier.Infrastructure -s src/HhAutoApplier.Api -o Persistence/Migrations
```

## Проверка на живой странице без отправки
- Открыть список/диалог можно; кнопку «Отправить» не нажимать
- Закрыть за собой (Escape / кнопка закрытия), временные скрипты удалить

## Антипаттерны
- ❌ «Быстрая проверка» откликом на реальную вакансию
- ❌ Новая зависимость ради одной функции
