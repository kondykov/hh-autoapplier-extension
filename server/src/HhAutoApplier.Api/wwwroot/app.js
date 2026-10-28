"use strict";
// Единый веб-интерфейс (#46): разделы по хешу адреса поверх того же API, что у расширения. Вход — пароль, дальше cookie.
// Администратор может смотреть данные другого пользователя (?userId=) — только чтение: правки API делает от своего имени

const view = document.getElementById("view");
const status = document.getElementById("status");
const loginForm = document.getElementById("login");
const nav = document.getElementById("nav");
const viewAs = document.getElementById("view-as");

let me = null;
// Чьи данные показываем: null — свои
let viewedUser = null;

class Unauthorized extends Error {}

function setStatus(text, error = false) {
  status.textContent = text;
  status.className = error ? "error" : "";
}

async function api(path, options = {}) {
  const res = await fetch(path, { ...options, headers: { "Content-Type": "application/json" } });
  const body = res.status === 204 ? null : await res.json().catch(() => null);
  if (res.status === 401) throw new Unauthorized(body?.message ?? "нужен вход");
  if (res.status === 403) throw new Error(body?.message ?? "нет прав");
  // Ошибки проверки полей (400) — списком по полям
  const fields = body?.errors ? Object.values(body.errors).flat().join("; ") : null;
  if (!res.ok) throw new Error(body?.message ?? fields ?? body?.title ?? `сервер ответил ${res.status}`);
  return body;
}

const post = (path, body) => api(path, { method: "POST", body: body === undefined ? undefined : JSON.stringify(body) });
const put = (path, body) => api(path, { method: "PUT", body: JSON.stringify(body) });

// Чтение с учётом выбранного пользователя
function scoped(path) {
  if (!viewedUser) return path;
  return `${path}${path.includes("?") ? "&" : "?"}userId=${encodeURIComponent(viewedUser)}`;
}

// Данные — только текстом: вакансии и письма приходят с hh.ru и от LLM
function el(tag, props = {}, ...children) {
  const node = document.createElement(tag);
  for (const [key, value] of Object.entries(props)) {
    if (key.startsWith("data-") || key === "for" || key === "aria-label") node.setAttribute(key, value);
    else node[key] = value;
  }
  for (const child of children.flat()) if (child != null && child !== false) node.append(child);
  return node;
}

// Ссылка — только http(s): javascript: и прочее из данных не попадает в href
function safeUrl(url) {
  try {
    const u = new URL(url);
    return u.protocol === "https:" || u.protocol === "http:" ? u.href : "#";
  } catch {
    return "#";
  }
}

const date = (value) => (value ? new Date(value).toLocaleString("ru-RU", { dateStyle: "short", timeStyle: "short" }) : "");
const readOnly = () => viewedUser !== null;

const STATUSES = {
  New: "новая", Pending: "ждёт оценки", Review: "на проверке", Questionnaire: "анкета", Queued: "в очереди",
  Applied: "отклик отправлен", Invited: "приглашение", Rejected: "отказ", Skipped: "пропущена", Failed: "ошибка",
};

// Действие с кнопкой: на время запроса кнопка выключена, итог — в строке статуса
async function run(button, action, done) {
  button.disabled = true;
  try {
    const result = await action();
    if (done) setStatus(typeof done === "function" ? done(result) : done);
    return result;
  } catch (e) {
    if (e instanceof Unauthorized) showLogin();
    setStatus(e.message, true);
  } finally {
    button.disabled = false;
  }
}

// ── Разделы ──────────────────────────────────────────────────────────

async function overview() {
  const [stats, telegram] = await Promise.all([api(scoped("/api/stats")), readOnly() ? null : api("/api/telegram").catch(() => null)]);
  const order = ["Queued", "Review", "Questionnaire", "Pending", "Applied", "Invited", "Rejected", "Skipped", "Failed", "New"];
  const total = Object.values(stats).reduce((a, b) => a + b, 0);
  const tiles = order.filter((s) => stats[s]).map((s) =>
    el("a", { className: "tile", href: `#vacancies/${s}`, style: "text-decoration:none;color:inherit" }, el("b", { textContent: stats[s] }), el("span", { textContent: STATUSES[s] })));

  return [
    el("h1", { textContent: "Обзор" }),
    el("p", { className: "sub", textContent: `Всего откликов в работе: ${total}. Нажмите на счётчик — откроется список.` }),
    tiles.length ? el("div", { className: "grid" }, tiles) : el("p", { className: "empty", textContent: "Откликов пока нет: добавьте поиск — сервер соберёт вакансии." }),
    el("div", { className: "card" },
      el("h2", { textContent: "Что дальше" }),
      el("ul", {},
        stats.Review ? el("li", {}, `На проверке ${stats.Review}: `, el("a", { href: "review.html", textContent: "решить" })) : null,
        el("li", {}, "Ответы работодателей — ", el("a", { href: "dialogs.html", textContent: "приглашения и вопросы с черновиками" })),
        el("li", {}, "Отчёт по откликам — ", el("a", { href: scoped("/api/report?format=csv"), textContent: "CSV" })),
        telegram?.configured && !readOnly() ? el("li", {}, telegram.linked ? `Telegram привязан${telegram.username ? ` (@${telegram.username})` : ""}` : "Telegram не привязан — ",
          telegram.linked ? null : el("a", { href: "#account", textContent: "привязать в профиле" })) : null)),
  ];
}

async function vacancies(statusFilter) {
  const take = 50;
  const searches = await api(scoped("/api/searches"));
  const state = { status: statusFilter ?? "", search: "", skip: 0 };

  const statusSelect = el("select", { "aria-label": "Статус" }, el("option", { value: "", textContent: "Все статусы" }),
    Object.entries(STATUSES).map(([value, text]) => el("option", { value, textContent: text })));
  statusSelect.value = state.status;
  const searchSelect = el("select", { "aria-label": "Поиск" }, el("option", { value: "", textContent: "Все поиски" }),
    searches.map((s) => el("option", { value: s.name, textContent: s.name })));
  const prev = el("button", { textContent: "← Назад" });
  const next = el("button", { textContent: "Дальше →" });
  const page = el("span", { className: "muted" });
  const list = el("div");

  async function load() {
    const query = new URLSearchParams({ skip: state.skip, take });
    if (state.status) query.set("status", state.status);
    if (state.search) query.set("search", state.search);
    try {
      const items = await api(scoped(`/api/vacancies?${query}`));
      list.replaceChildren(...(items.length ? items.map(vacancyCard) : [el("p", { className: "empty", textContent: "Ничего нет." })]));
      prev.disabled = state.skip === 0;
      next.disabled = items.length < take;
      page.textContent = items.length ? `${state.skip + 1}–${state.skip + items.length}` : "";
    } catch (e) {
      setStatus(e.message, true);
    }
  }

  statusSelect.onchange = () => { state.status = statusSelect.value; state.skip = 0; load(); };
  searchSelect.onchange = () => { state.search = searchSelect.value; state.skip = 0; load(); };
  prev.onclick = () => { state.skip = Math.max(0, state.skip - take); load(); };
  next.onclick = () => { state.skip += take; load(); };
  await load();

  return [el("h1", { textContent: "Вакансии" }), el("div", { className: "bar" }, statusSelect, searchSelect, prev, next, page), list];
}

function salary(v) {
  if (v.salaryFrom == null && v.salaryTo == null) return null;
  const range = [v.salaryFrom != null ? `от ${v.salaryFrom.toLocaleString("ru-RU")}` : "", v.salaryTo != null ? `до ${v.salaryTo.toLocaleString("ru-RU")}` : ""];
  return `${range.filter(Boolean).join(" ")} ${v.salaryCurrency ?? ""}`.trim();
}

function vacancyCard(v) {
  return el("article", { className: "card" },
    el("div", {}, el("a", { href: safeUrl(v.url), target: "_blank", rel: "noopener", textContent: v.title }), " ",
      el("span", { className: "pill", textContent: STATUSES[v.status] ?? v.status })),
    el("div", { className: "meta", textContent: [v.company, v.area, salary(v), v.workFormat, `балл ${v.llmScore ?? v.score}`, v.resumeProfile && `резюме ${v.resumeProfile}`, v.searchName && `поиск ${v.searchName}`, v.appliedAt && `отклик ${date(v.appliedAt)}`].filter(Boolean).join(" · ") }),
    v.statusReason || v.llmReason ? el("div", { className: "reason", textContent: v.statusReason ?? v.llmReason }) : null,
    v.coverLetter ? el("details", {}, el("summary", { textContent: "Письмо" }), el("div", { className: "pre", textContent: v.coverLetter })) : null,
    v.history?.length ? el("details", {}, el("summary", { textContent: `История (${v.history.length})` }),
      el("table", {}, v.history.map((h) => el("tr", {}, el("td", { textContent: date(h.at) }), el("td", { textContent: STATUSES[h.to] ?? h.to }), el("td", { textContent: h.reason ?? "" }))))) : null);
}

const lines = (text) => text.split(/[\n,]/).map((s) => s.trim()).filter(Boolean);

function field(label, input, wide = false) {
  input.id ||= `f-${Math.random().toString(36).slice(2)}`;
  return wide ? [el("label", { for: input.id, className: "wide", textContent: label }), Object.assign(input, { className: "wide" })]
    : [el("label", { for: input.id, textContent: label }), input];
}

async function searches() {
  const items = await api(scoped("/api/searches"));
  const list = items.map((s) => searchForm(s));
  if (!readOnly()) list.push(searchForm(null));
  return [
    el("h1", { textContent: "Поиски" }),
    el("p", { className: "sub", textContent: "Сервер собирает вакансии по включённым поискам раз в час и отбирает по правилам; остальное оценивает LLM." }),
    list,
  ];
}

function searchForm(s) {
  const r = s?.rules ?? {};
  const inputs = {
    name: el("input", { value: s?.name ?? "", required: true }),
    query: el("input", { value: s?.query ?? "", required: true }),
    area: el("input", { value: s?.area ?? "", placeholder: "113 — Россия, 1 — Москва" }),
    extraParams: el("input", { value: s?.extraParams ?? "", placeholder: "work_format=REMOTE&experience=between3And6" }),
    enabled: el("input", { type: "checkbox", checked: s?.enabled ?? true }),
    includeKeywords: el("input", { value: (r.includeKeywords ?? []).join(", ") }),
    excludeKeywords: el("input", { value: (r.excludeKeywords ?? []).join(", ") }),
    allowedFormats: el("input", { value: (r.allowedFormats ?? []).join(", "), placeholder: "Удалённо, Гибрид" }),
    minSalaryRub: el("input", { type: "number", value: r.minSalaryRub ?? "" }),
    minScore: el("input", { type: "number", value: r.minScore ?? 0 }),
    requireSalary: el("input", { type: "checkbox", checked: !!r.requireSalary }),
    skipWithTest: el("input", { type: "checkbox", checked: !!r.skipWithTest }),
    skipReview: el("input", { type: "checkbox", checked: !!r.skipReview }),
  };
  if (readOnly()) Object.values(inputs).forEach((i) => (i.disabled = true));

  const save = el("button", { className: "primary", textContent: s ? "Сохранить" : "Добавить" });
  const remove = s ? el("button", { className: "danger", textContent: "Удалить", type: "button" }) : null;
  const form = el("form", { className: "card" },
    el("h2", { textContent: s ? s.name : "Новый поиск" }),
    el("div", { className: "form" },
      field("Название", inputs.name), field("Запрос", inputs.query), field("Регион (area)", inputs.area), field("Доп. параметры", inputs.extraParams),
      field("Включён", inputs.enabled), field("Нужные слова", inputs.includeKeywords), field("Исключить слова", inputs.excludeKeywords),
      field("Форматы работы", inputs.allowedFormats), field("Зарплата от, ₽", inputs.minSalaryRub), field("Мин. балл", inputs.minScore),
      field("Только с зарплатой", inputs.requireSalary), field("Пропускать с тестом", inputs.skipWithTest), field("Без ручной проверки", inputs.skipReview)),
    readOnly() ? null : el("div", { className: "bar", style: "margin:12px 0 0" }, save, remove));

  form.onsubmit = (e) => {
    e.preventDefault();
    run(save, () => put("/api/searches", {
      name: inputs.name.value.trim(), query: inputs.query.value.trim(), area: inputs.area.value.trim() || null,
      extraParams: inputs.extraParams.value.trim() || null, enabled: inputs.enabled.checked,
      rules: {
        includeKeywords: lines(inputs.includeKeywords.value), excludeKeywords: lines(inputs.excludeKeywords.value),
        allowedFormats: lines(inputs.allowedFormats.value), minSalaryRub: inputs.minSalaryRub.value ? Number(inputs.minSalaryRub.value) : null,
        minScore: Number(inputs.minScore.value || 0), requireSalary: inputs.requireSalary.checked,
        skipWithTest: inputs.skipWithTest.checked, skipReview: inputs.skipReview.checked,
      },
    }), (saved) => { render(); return `Поиск «${saved.name}» сохранён`; });
  };
  if (remove) remove.onclick = () => {
    if (confirm(`Удалить поиск «${s.name}»?`))
      run(remove, () => api(`/api/searches/${s.id}`, { method: "DELETE" }), () => { render(); return `Поиск «${s.name}» удалён`; });
  };
  return form;
}

async function resumes() {
  const items = await api(scoped("/api/resumes"));
  const list = items.map((r) => resumeForm(r));
  if (!readOnly()) list.push(resumeForm(null));
  return [
    el("h1", { textContent: "Резюме" }),
    el("p", { className: "sub", textContent: "Профили резюме: «совпадение» — часть названия резюме на hh.ru, по ней расширение выбирает резюме. Описание и шаблон письма читает LLM." }),
    list,
  ];
}

function resumeForm(r) {
  const inputs = {
    name: el("input", { value: r?.name ?? "", required: true }),
    match: el("input", { value: r?.match ?? "", required: true }),
    keywords: el("input", { value: (r?.keywords ?? []).join(", ") }),
    summary: el("textarea", { value: r?.summary ?? "" }),
    letterTemplate: el("textarea", { value: r?.letterTemplate ?? "" }),
  };
  if (readOnly()) Object.values(inputs).forEach((i) => (i.disabled = true));

  const save = el("button", { className: "primary", textContent: r ? "Сохранить" : "Добавить" });
  const form = el("form", { className: "card" },
    el("h2", { textContent: r ? r.name : "Новый профиль резюме" }),
    el("div", { className: "form" },
      field("Название", inputs.name), field("Совпадение", inputs.match), field("Ключевые слова", inputs.keywords),
      field("Описание для LLM", inputs.summary, true), field("Шаблон письма", inputs.letterTemplate, true)),
    readOnly() ? null : el("div", { className: "bar", style: "margin:12px 0 0" }, save));

  form.onsubmit = (e) => {
    e.preventDefault();
    run(save, () => put("/api/resumes", {
      name: inputs.name.value.trim(), match: inputs.match.value.trim(), keywords: lines(inputs.keywords.value),
      summary: inputs.summary.value.trim() || null, letterTemplate: inputs.letterTemplate.value.trim() || null,
    }), (saved) => { render(); return `Резюме «${saved.name}» сохранено`; });
  };
  return form;
}

async function account() {
  const [tokens, telegram] = await Promise.all([api("/api/tokens"), api("/api/telegram")]);

  // Пароль
  const current = el("input", { type: "password", autocomplete: "current-password", required: true });
  const fresh = el("input", { type: "password", autocomplete: "new-password", required: true, minLength: 8 });
  const change = el("button", { className: "primary", textContent: "Сменить пароль" });
  const passwordForm = el("form", { className: "card" }, el("h2", { textContent: "Пароль" }),
    el("div", { className: "form" }, field("Текущий", current), field("Новый", fresh)),
    el("div", { className: "bar", style: "margin:12px 0 0" }, change),
    el("p", { className: "muted", textContent: "Остальные сессии будут завершены." }));
  passwordForm.onsubmit = (e) => {
    e.preventDefault();
    run(change, () => post("/api/auth/password", { currentPassword: current.value, newPassword: fresh.value }),
      () => { current.value = fresh.value = ""; return "Пароль изменён"; });
  };

  // Персональные токены — для расширения
  const tokenName = el("input", { placeholder: "Название, например «ноутбук»", "aria-label": "Название токена", required: true });
  const issue = el("button", { className: "primary", textContent: "Выпустить" });
  const issued = el("div");
  const tokenForm = el("form", { className: "bar" }, tokenName, issue);
  tokenForm.onsubmit = async (e) => {
    e.preventDefault();
    const token = await run(issue, () => post("/api/tokens", { name: tokenName.value.trim() }), "Токен выпущен");
    if (!token) return;
    tokenName.value = "";
    tokensTable.replaceWith(tokensTable = tokenRows(await api("/api/tokens")));
    issued.replaceChildren(el("p", { textContent: "Скопируйте токен в панель расширения — он показывается один раз:" }), el("div", { className: "secret", textContent: token.value }));
  };
  let tokensTable = tokenRows(tokens);

  function tokenRows(items) {
    if (!items.length) return el("p", { className: "muted", textContent: "Токенов нет." });
    return el("table", {}, el("tr", {}, el("th", { textContent: "Название" }), el("th", { textContent: "Начало" }), el("th", { textContent: "Выпущен" }), el("th")),
      items.map((t) => {
        const revoke = el("button", { className: "danger", textContent: "Отозвать" });
        revoke.onclick = () => {
          if (confirm(`Отозвать токен «${t.name}»? Расширение с ним перестанет работать.`))
            run(revoke, () => api(`/api/tokens/${t.id}`, { method: "DELETE" }), () => { render(); return `Токен «${t.name}» отозван`; });
        };
        return el("tr", {}, el("td", { textContent: t.name }), el("td", { textContent: `${t.prefix}…` }), el("td", { textContent: date(t.createdAt) }),
          el("td", {}, t.revokedAt ? `отозван ${date(t.revokedAt)}` : revoke));
      }));
  }

  // Telegram
  const tgBox = el("div");
  function telegramView(t) {
    if (!t.configured) return el("p", { className: "muted", textContent: "Бот не настроен на сервере (TELEGRAM_BOT_TOKEN)." });
    if (t.linked) {
      const unlink = el("button", { className: "danger", textContent: "Отвязать" });
      unlink.onclick = () => run(unlink, () => api("/api/telegram", { method: "DELETE" }), () => { render(); return "Telegram отвязан"; });
      return el("div", { className: "bar" }, `Привязан${t.username ? ` к @${t.username}` : ""} ${date(t.linkedAt)}`, unlink);
    }
    const link = el("button", { className: "primary", textContent: "Получить ссылку привязки" });
    const out = el("div");
    link.onclick = async () => {
      const code = await run(link, () => post("/api/telegram/link"), "Код действует 15 минут");
      if (!code) return;
      out.replaceChildren(code.link
        ? el("p", {}, "Откройте ", el("a", { href: safeUrl(code.link), target: "_blank", rel: "noopener", textContent: "ссылку на бота" }), " и нажмите «Start».")
        : el("p", {}, "Отправьте боту: ", el("span", { className: "secret", textContent: `/start ${code.code}` })));
    };
    return el("div", {}, el("div", { className: "bar" }, "Не привязан", link), out);
  }
  tgBox.append(telegramView(telegram));

  return [
    el("h1", { textContent: "Профиль" }),
    el("p", { className: "sub", textContent: `${me.name}, роль ${me.role}` }),
    me.viaApiKey ? el("p", { className: "card", textContent: "Вход по токену: пароль, токены и Telegram меняются только после входа по паролю." }) : passwordForm,
    el("div", { className: "card" }, el("h2", { textContent: "Токены расширения" }), tokenForm, issued, tokensTable),
    el("div", { className: "card" }, el("h2", { textContent: "Telegram" }), tgBox),
    el("div", { className: "card" }, el("h2", { textContent: "Отчёт" }), el("a", { href: "/api/report?format=csv", textContent: "Скачать отчёт по откликам (CSV)" })),
  ];
}

async function admin() {
  if (me.role !== "Admin") return [el("p", { className: "empty", textContent: "Раздел только для администратора." })];
  const users = await api("/api/users");

  const rows = users.map((u) => {
    const self = u.id === me.id;
    const role = el("select", { disabled: self, "aria-label": "Роль" }, el("option", { value: "User", textContent: "User" }), el("option", { value: "Admin", textContent: "Admin" }));
    role.value = u.role;
    role.onchange = () => run(role, () => put(`/api/users/${u.id}/role`, { role: role.value }), `${u.email}: роль ${role.value}`);
    const block = el("button", { textContent: u.blocked ? "Разблокировать" : "Заблокировать", className: u.blocked ? "" : "danger", disabled: self });
    block.onclick = () => run(block, () => put(`/api/users/${u.id}/blocked`, { blocked: !u.blocked }), () => { render(); return `${u.email}: ${u.blocked ? "разблокирован" : "заблокирован"}`; });
    const reset = el("button", { textContent: "Задать пароль" });
    reset.onclick = () => {
      const password = prompt(`Новый пароль для ${u.email} (не короче 8 символов)`);
      if (password) run(reset, () => put(`/api/users/${u.id}/password`, { password }), `${u.email}: пароль задан`);
    };
    return el("tr", {}, el("td", { textContent: u.email }), el("td", {}, role), el("td", { textContent: date(u.createdAt) }),
      el("td", {}, el("div", { className: "bar", style: "margin:0" }, block, reset)));
  });

  const email = el("input", { type: "email", placeholder: "Email", "aria-label": "Email", required: true });
  const password = el("input", { type: "password", placeholder: "Пароль", "aria-label": "Пароль", autocomplete: "new-password", required: true, minLength: 8 });
  const newRole = el("select", { "aria-label": "Роль" }, el("option", { value: "User", textContent: "User" }), el("option", { value: "Admin", textContent: "Admin" }));
  const create = el("button", { className: "primary", textContent: "Создать" });
  const createForm = el("form", { className: "bar" }, email, password, newRole, create);
  createForm.onsubmit = (e) => {
    e.preventDefault();
    run(create, () => post("/api/users", { email: email.value.trim(), password: password.value, role: newRole.value }),
      () => { render(); loadUsers(); return `Пользователь ${email.value.trim()} создан`; });
  };

  // Ручной запуск фоновых проходов: обычно они идут сами (сбор — раз в час, LLM — раз в минуту при наличии работы)
  const jobs = [
    ["Собрать вакансии", "/api/collect", undefined, (r) => `Сбор: поисков ${r.searches}, новых ${r.added}, в очередь ${r.queued}, пропущено ${r.skipped}`],
    ["Проверить LLM", "/api/llm/check", undefined, (r) => `LLM ${r.model}: ответ за ${r.latencyMs} мс`],
    ["Оценить ожидающие", "/api/llm/evaluate", { limit: 20 }, (r) => `Оценено ${r.evaluated}: подходят ${r.fit}, сомнительных ${r.doubtful}, отклонено ${r.rejected}; осталось ${r.remaining}`],
    ["Ответить на анкеты", "/api/llm/answers", { limit: 20 }, (r) => `Анкеты: отвечено ${r.answered}, не вышло ${r.failed}; осталось ${r.remaining}`],
    ["Написать письма", "/api/llm/letters", { limit: 20 }, (r) => `Письма: написано ${r.written}, не вышло ${r.failed}; осталось ${r.remaining}`],
  ].map(([text, path, body, done]) => {
    const button = el("button", { textContent: text });
    button.onclick = () => { setStatus(`${text}…`); run(button, () => post(path, body), done); };
    return button;
  });

  return [
    el("h1", { textContent: "Администрирование" }),
    el("div", { className: "card" }, el("h2", { textContent: "Пользователи" }),
      el("table", {}, el("tr", {}, el("th", { textContent: "Email" }), el("th", { textContent: "Роль" }), el("th", { textContent: "Создан" }), el("th")), rows),
      el("h2", { textContent: "Новый пользователь", style: "margin-top:16px" }), createForm),
    el("div", { className: "card" }, el("h2", { textContent: "Запустить сейчас" }),
      el("p", { className: "muted", textContent: "Обычно не нужно: сбор идёт раз в час, LLM — раз в минуту, если есть работа. Тратит лимит LLM и запросы к hh.ru." }),
      el("div", { className: "bar", style: "margin:0" }, jobs)),
  ];
}

// ── Каркас ───────────────────────────────────────────────────────────

const routes = { overview, vacancies, searches, resumes, account, admin };

// Номер последней отрисовки: раздел, загрузившийся позже следующего перехода, не перерисовывает страницу
let renders = 0;

async function render() {
  if (!me) return;
  const current = ++renders;
  const [name, arg] = location.hash.slice(1).split("/");
  const route = routes[name] ? name : "overview";
  for (const a of nav.querySelectorAll("a")) a.classList.toggle("active", a.getAttribute("href") === `#${route}`);
  try {
    const content = await routes[route](arg);
    if (current !== renders) return;
    view.replaceChildren(...[content].flat(Infinity).filter(Boolean));
  } catch (e) {
    if (current !== renders) return;
    view.replaceChildren();
    if (e instanceof Unauthorized) showLogin();
    setStatus(e.message, true);
  }
}

async function loadUsers() {
  if (me.role !== "Admin") return;
  const users = await api("/api/users").catch(() => []);
  viewAs.replaceChildren(el("option", { value: "", textContent: "мои" }),
    ...users.filter((u) => u.id !== me.id).map((u) => el("option", { value: u.id, textContent: u.email })));
  viewAs.value = viewedUser ?? "";
  document.getElementById("viewing").hidden = users.length < 2;
}

function showLogin() {
  me = null;
  view.replaceChildren();
  nav.hidden = true;
  document.getElementById("logout").hidden = true;
  document.getElementById("viewing").hidden = true;
  document.getElementById("who").textContent = "";
  loginForm.hidden = false;
  document.getElementById("email").focus();
}

async function showSession(user) {
  me = user;
  loginForm.hidden = true;
  nav.hidden = false;
  document.getElementById("logout").hidden = false;
  document.getElementById("who").textContent = user.name;
  for (const a of nav.querySelectorAll("[data-admin]")) a.hidden = user.role !== "Admin";
  await loadUsers();
  await render();
}

loginForm.onsubmit = async (e) => {
  e.preventDefault();
  const password = document.getElementById("password");
  try {
    await post("/api/auth/login", { email: document.getElementById("email").value.trim(), password: password.value });
    password.value = "";
    setStatus("");
    // Вход отвечает учётной записью без признака входа по токену — берём то же, что при открытии страницы
    await showSession(await api("/api/auth/me"));
  } catch (err) {
    setStatus(err.message, true);
  }
};

document.getElementById("logout").onclick = async () => {
  await post("/api/auth/logout").catch(() => null);
  setStatus("");
  showLogin();
};

viewAs.onchange = () => {
  viewedUser = viewAs.value || null;
  setStatus(viewedUser ? `Данные ${viewAs.selectedOptions[0].textContent} — только просмотр` : "");
  render();
};

window.addEventListener("hashchange", () => { setStatus(viewedUser ? status.textContent : ""); render(); });

// Сессия могла остаться с прошлого раза
api("/api/auth/me").then(showSession).catch(() => showLogin());
