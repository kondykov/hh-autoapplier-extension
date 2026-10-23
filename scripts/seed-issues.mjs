// Одноразовый: создаёт метки, milestones и issues в GitHub по docs/ROADMAP.md. Запуск: node scripts/seed-issues.mjs [--dry]. После создания issues удалить.
// Токен берётся из Git Credential Manager (git credential fill), в вывод не попадает.
import { execFileSync } from "node:child_process";
import { writeFileSync } from "node:fs";

const REPO = "kondykov/hh-autoapplier-extension";
const DRY = process.argv.includes("--dry");
const cred = DRY ? "" : execFileSync("git", ["credential", "fill"], { input: "protocol=https\nhost=github.com\n\n" }).toString();
const token = /password=(.*)/.exec(cred)?.[1]?.trim();
if (!token && !DRY) throw new Error("нет токена GitHub в Git Credential Manager");

async function gh(method, path, body) {
  if (DRY) return { number: 0, html_url: "" };
  const r = await fetch(`https://api.github.com/repos/${REPO}${path}`, {
    method,
    headers: { Authorization: `Bearer ${token}`, Accept: "application/vnd.github+json", "User-Agent": "hh-autoapplier-setup" },
    body: body && JSON.stringify(body),
  });
  if (r.status === 422) return null; // уже существует
  if (!r.ok) throw new Error(`${method} ${path}: ${r.status} ${await r.text()}`);
  return r.json();
}

const LABELS = [
  ["backend", "1d76db", "Backend (ASP.NET Core)"],
  ["extension", "0e8a16", "Расширение Chrome"],
  ["llm", "5319e7", "LLM и агенты"],
  ["rbac", "b60205", "Пользователи и доступ"],
  ["infra", "c5def5", "Сервер, CI, VPN"],
  ["docs", "0075ca", "Документация"],
  ["feature", "a2eeef", "Новая возможность"],
  ["chore", "ededed", "Рефакторинг, тесты, обслуживание"],
  ["question", "d876e3", "Нужно решение владельца"],
  ["p1", "d93f0b", "Приоритет: высокий"],
  ["p2", "fbca04", "Приоритет: средний"],
  ["p3", "cfd3d7", "Приоритет: низкий"],
];

const MILESTONES = [
  ["1. Основа", "CI, живые проверки, ADR"],
  ["2. Сервер дома", "Docker на домашнем сервере, VPN, firewall, TLS, бэкапы"],
  ["3. Пользователи", "Identity, роли, отклик пользователя отдельно от вакансии, модули backend"],
  ["4. Расширение — клиент", "Связь с backend, импорт, режим «из очереди», тесты"],
  ["5. LLM", "Оценка, письма, ответы на анкеты, ручная проверка, диалоги"],
  ["6. Автономность", "Telegram, Playwright-воркер"],
];

const R = (section) => `\n\nПодробности: docs/ROADMAP.md, ${section}.`;
// [milestone index | null, labels, title, body]
const ISSUES = [
  [0, "infra,chore,p1", "CI на GitHub Actions", "Прогон на push и PR: `npm run typecheck`, `npm test`, `npm run build`, `cd server; dotnet test`." + R("«Технический долг»")],
  [0, "extension,chore,p1", "Живая проверка выбора резюме и автопаузы при капче", "Не проверено на живом hh.ru: клик по резюме в списке (селекторы подтверждены) и автопауза при капче. Проверять без отправки отклика." + R("«Где проект сейчас»")],
  [0, "docs,p2", "ADR: архитектура backend и расширения", "Записать решения в `docs/adr/`: модульный монолит на backend, порты и адаптеры в расширении, OpenAPI как контракт." + R("раздел D")],
  [0, "question,docs", "Решения владельца по плану", "- Какой LLM: облачный, локальный, opencode? Есть ли GPU?\n- Ручная проверка каждого отклика или только сомнительных?\n- Нужен ли веб-интерфейс проверки?\n- ОС сервера и роутер (VLAN)? Tailscale или своё (Headscale/WireGuard)?\n- Диалоги: только подсказка ответа или и отправка?" + R("«Открытые вопросы владельцу»")],
  [1, "infra,p1", "Развернуть backend на домашнем сервере", "Docker compose на сервере, автозапуск, бэкап БД, обновления ОС и контейнеров, логи." + R("раздел E")],
  [1, "infra,p1", "Доступ из WAN через VPN, firewall и TLS", "Tailscale (или Headscale/WireGuard), без проброса портов API и БД. Firewall: входящие только из VPN на порт API; Postgres только внутри Docker; TLS; ограничение частоты входа." + R("раздел E")],
  [2, "backend,rbac,feature,p1", "Пользователи и роли (Identity, Admin/User)", "ASP.NET Core Identity, роли `Admin` и `User`, вход по логину и паролю. Новые зависимости и миграции согласовать." + R("раздел A")],
  [2, "backend,rbac,feature,p1", "Отклик пользователя (Application) отдельно от вакансии", "Вакансия общая, а балл, резюме, письмо, ответы, статус и история — в `Application` на пользователя. Сейчас `Status`, `Score`, `ResumeProfile` лежат в `Vacancy`: миграция данных." + R("раздел A")],
  [2, "backend,rbac,feature,p1", "Владельцы у поисков, резюме, правил и писем", "Все запросы API фильтруются по текущему пользователю, `Admin` видит всё." + R("раздел A")],
  [2, "backend,rbac,feature,p2", "Персональные токены для расширения", "Вместо общего `X-Api-Key`: токен на пользователя, можно отозвать." + R("раздел A")],
  [2, "backend,chore,p2", "Разбить backend на модули, вынести логику из Program.cs", "Модули Identity, Vacancies, Applications, Resumes, Letters, Llm, Collecting; эндпоинты по модулям; импорт и CSV — из `Program.cs` в сервисы." + R("раздел D")],
  [2, "backend,chore,p2", "Интеграционные тесты API", "`WebApplicationFactory` и тестовый Postgres: импорт, очередь, статусы, доступ по ролям и токенам." + R("раздел D")],
  [3, "extension,feature,p1", "Настройки сервера и токена в расширении", "Адрес backend и персональный токен в панели, `host_permissions` в `manifest.json`, клиент API." + R("раздел C")],
  [3, "extension,backend,feature,p1", "Импорт карточек с выдачи в /api/import", "Пока нет токена API hh.ru, расширение отправляет собранные карточки на сервер." + R("раздел C")],
  [3, "extension,feature,p1", "Режим «из очереди»: исполнение одобренных откликов", "Получить следующий одобренный отклик → открыть вакансию → выбрать резюме → вставить письмо и ответы → отправить → статус и исход на сервер. Лимит, задержки и автопауза остаются." + R("раздел C")],
  [3, "backend,p2", "Токен приложения hh.ru для сборщика", "`api.hh.ru` отвечает 403 без токена: зарегистрировать приложение на dev.hh.ru, `HH_ACCESS_TOKEN` и реальный `HH_USER_AGENT`." + R("«Где проект сейчас»")],
  [3, "extension,chore,p2", "Тесты на конвейер отклика", "HTML-фикстуры диалога отклика и списка резюме; тесты `ApplyPipeline`, `submit`, `ResumeSelector`." + R("«Технический долг»")],
  [3, "extension,chore,p2", "Разделить Controller на шаги страниц (PageStep)", "Выдача, вакансия, страница отклика — отдельные шаги; ядро без DOM и `chrome.*`." + R("раздел D")],
  [3, "extension,p3", "Случайный разброс задержек", "`DELAYS` фиксированные; добавить человекоподобный разброс. Меняет частоту запросов — согласовать." + R("«Технический долг»")],
  [3, "backend,infra,p3", "Ограничить CORS источником расширения", "Сейчас `AllowAnyOrigin`; разрешить только `chrome-extension://<id>`." + R("«Технический долг»")],
  [4, "llm,backend,feature,p1", "ILlmClient и провайдеры", "Интерфейс в Core; реализации: OpenAI-совместимое API и агент opencode (`opencode serve`). Фоновые задачи с повторами, лимит расходов, сохранение промпта и версии модели." + R("раздел B")],
  [4, "llm,backend,feature,p1", "LLM-оценка вакансии под резюме", "Балл и объяснение после быстрых правил; выбор резюме." + R("раздел B")],
  [4, "llm,backend,feature,p1", "Сопроводительное письмо под вакансию", "Генерация на backend по резюме и тексту вакансии." + R("раздел B")],
  [4, "llm,backend,extension,feature,p2", "Ответы на анкеты работодателей", "Backend готовит ответы, расширение вставляет. Логика сопоставления вопросов — в `docs/ARCHITECTURE.md`, «Автозаполнение анкет»." + R("раздел B")],
  [4, "backend,feature,p2", "Ручная проверка откликов", "Одобрить, поправить письмо, отклонить; режим без проверки включается правилом пользователя." + R("раздел B")],
  [4, "llm,backend,feature,p3", "Разбор диалогов с работодателями", "Анализ ответов работодателей и предложение ответа." + R("раздел B")],
  [4, "extension,chore,p3", "Убрать локальные фильтры, скоринг и шаблоны из расширения", "После перехода на режим «из очереди» правила живут только на backend; сейчас они продублированы и расходятся." + R("«Технический долг»")],
  [5, "backend,feature,p2", "Telegram: уведомления и управление", "События сервера (капча, анкета, ответ работодателя, лимит), ответы на них и команды." + R("MULTI_ACCOUNT_PLAN.md")],
  [5, "infra,feature,p3", "Playwright-воркер как альтернативный клиент", "То же ядро, что в расширении, под Playwright на сервере." + R("MULTI_ACCOUNT_PLAN.md")],
  [null, "chore,p2", "Вычистить личные данные коллеги из истории git", "В истории есть старые скрипты `legacy/` с именем и контактами коллеги. Перед публикацией репозитория — переписать историю (git filter-repo)." + R("«Технический долг»")],
];

for (const [name, color, description] of LABELS) await gh("POST", "/labels", { name, color, description });
const ms = [];
for (const [title, description] of MILESTONES) {
  const created = await gh("POST", "/milestones", { title, description });
  ms.push(created?.number ?? (await gh("GET", "/milestones?state=all"))?.find?.((m) => m.title === title)?.number);
}
const out = [];
for (const [m, labels, title, body] of ISSUES) {
  const res = await gh("POST", "/issues", { title, body, labels: labels.split(","), ...(m === null ? {} : { milestone: ms[m] }) });
  out.push(`#${res.number}\t${m === null ? "-" : MILESTONES[m][0]}\t${title}`);
  console.log(out.at(-1));
}
writeFileSync("issues-created.tsv", out.join("\n"));
