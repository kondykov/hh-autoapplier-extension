// Живой прогон backend на настоящих вакансиях: одноразовая Postgres, API из текущего дерева, настоящий hh.ru и LLM. Откликов не отправляет.
// node scripts/real-check.mjs --env server/.env --query "NAME:(Junior AND C#)" --ids 1,2 --report отчёт.md
// Из --env читаются только LLM_*; значения .env, пароли и cookie не печатаются и не попадают в отчёт.
// Расход: запросы к hh.ru — страница выдачи и страницы вакансий (выбирать запрос с короткой выдачей), LLM — не больше LLM_LIMIT.
import { execFileSync, spawn, spawnSync } from "node:child_process";
import { randomBytes } from "node:crypto";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import http from "node:http";
import net from "node:net";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { parseArgs } from "node:util";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const DB_NAME = "hh-real-db";
// Вне динамического диапазона 49152+: Windows резервирует там блоки портов под Hyper-V
const DB_PORT = 15433;
const API_PORT = 8093;
const API = `127.0.0.1:${API_PORT}`;
const SEARCH = "real-check";
const LLM_LIMIT = 30;
// Предохранитель: выдача оказалась длиннее ожидаемой — API останавливается, а не обходит её до конца
const HH_LIMIT = 30;
const PIPELINE_WAIT_MS = 8 * 60_000;
const REEVALUATE_WAIT_MS = 3 * 60_000;
// Конвейер идёт раз в минуту: столько без новых запросов LLM — значит, писем больше не будет
const SETTLE_MS = 75_000;
const LEVEL_KEYWORDS = ["junior", "младший", "стажёр", "стажер"];
const START = "<<<ДАННЫЕ РАБОТОДАТЕЛЯ>>>";
const END = "<<<КОНЕЦ ДАННЫХ РАБОТОДАТЕЛЯ>>>";
// Что нужно самому dotnet; остальное окружение (и ключи из него) в API не передаётся
const SYSTEM_ENV = ["PATH", "PATHEXT", "SystemRoot", "SystemDrive", "windir", "ComSpec", "TEMP", "TMP", "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA",
  "ProgramData", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "NUGET_PACKAGES", "DOTNET_ROOT", "DOTNET_CLI_HOME"];
const RESUME = {
  name: "C# backend",
  match: "C#",
  keywords: ["C#", ".NET", "ASP.NET"],
  summary: "C#/.NET-разработчик, стаж 1 год 8 месяцев (вымышленный кандидат для проверки). ASP.NET Core, EF Core, PostgreSQL, Docker, xUnit, REST API. " +
    "Делал сервис учёта заказов: перевёл отчёты на индексы, время формирования снизилось на 30–40%. Покрывал обработчики тестами, настраивал CI в GitHub Actions. " +
    "Интересна backend-разработка на C#; фронтенд, 1С и Unity не интересны.",
};

const secrets = new Set();
const redact = (text) => {
  let out = String(text ?? "");
  for (const secret of secrets) out = out.split(secret).join("***");
  return out.replace(/hh_auth=[^;\s]+/g, "hh_auth=***");
};
const log = (message) => console.log(`[real-check] ${redact(message)}`);
const sleep = (ms) => new Promise((done) => setTimeout(done, ms));

function usage(message) {
  console.error(`${message}\nИспользование: node scripts/real-check.mjs --env <путь к server/.env> --query <текст поиска> [--ids 1,2,3] --report <файл отчёта>`);
  process.exit(2);
}

const { values: args } = parseArgs({ options: { env: { type: "string" }, query: { type: "string" }, ids: { type: "string" }, report: { type: "string" } } });
if (!args.env || !args.query || !args.report) usage("Нужны --env, --query и --report");
if (args.ids && !/^\d+(,\d+)*$/.test(args.ids)) usage("--ids — числа через запятую");
const ids = args.ids ? [...new Set(args.ids.split(","))] : [];

// Только LLM_*: остальные значения файла не попадают даже в память процесса
function readLlmEnv(path) {
  const env = {};
  for (const line of readFileSync(path, "utf8").split(/\r?\n/)) {
    const match = /^\s*(?:export\s+)?(LLM_[A-Z0-9_]+)\s*=\s*(.*)$/.exec(line);
    if (!match) continue;
    let value = match[2].trim();
    if (/^(["']).*\1$/.test(value)) value = value.slice(1, -1);
    else value = value.replace(/\s+#.*$/, "").trim();
    env[match[1]] = value;
  }

  return env;
}

const llm = readLlmEnv(resolve(args.env));
if (!llm.LLM_API_KEY) { console.error("[real-check] В файле --env нет LLM_API_KEY"); process.exit(2); }
for (const name of ["LLM_API_KEY", "LLM_BASE_URL", "LLM_MODEL"]) if (llm[name]) secrets.add(llm[name]);
const dbPassword = randomBytes(24).toString("hex");
const adminPassword = randomBytes(24).toString("hex");
secrets.add(dbPassword);
secrets.add(adminPassword);
const adminEmail = "admin@real-check.test";

// --- процессы ---

let apiChild = null;
let dbStarted = false;
let cleaned = false;
let hhOver = false;
const apiLines = [];
const counters = { hh: 0, llmHttp: 0, hhUrls: [] };

function killApi() {
  const child = apiChild;
  apiChild = null;
  if (!child?.pid) return;
  if (process.platform === "win32") spawnSync("taskkill", ["/PID", String(child.pid), "/T", "/F"], { stdio: "ignore" });
  else try { process.kill(-child.pid, "SIGKILL"); } catch { /* уже завершён */ }
}

// Синхронно: должно отработать и в обработчике exit
function cleanup() {
  if (cleaned) return;
  cleaned = true;
  killApi();
  if (dbStarted) {
    spawnSync("docker", ["stop", "-t", "3", DB_NAME], { stdio: "ignore" });
    spawnSync("docker", ["rm", "-f", DB_NAME], { stdio: "ignore" });
  }
}

process.on("exit", cleanup);
for (const signal of ["SIGINT", "SIGTERM", "SIGBREAK", "SIGHUP"])
  process.on(signal, () => { cleanup(); process.exit(130); });

const portFree = (port) => new Promise((done) => {
  const socket = net.connect({ host: "127.0.0.1", port });
  socket.setTimeout(1000);
  const finish = (free) => { socket.destroy(); done(free); };
  socket.once("connect", () => finish(false));
  socket.once("timeout", () => finish(true));
  socket.once("error", () => finish(true));
});

function startDb() {
  const existing = spawnSync("docker", ["ps", "-a", "--filter", `name=^${DB_NAME}$`, "--format", "{{.Names}}"], { encoding: "utf8" });
  if (existing.status !== 0) throw new Error("Docker недоступен");
  if (existing.stdout.trim()) throw new Error(`Контейнер ${DB_NAME} уже есть: он не наш, удалите его вручную`);
  dbStarted = true;
  // Пароль — через окружение docker: в командной строке его не видно
  const run = spawnSync("docker", ["run", "-d", "--rm", "--name", DB_NAME, "-p", `127.0.0.1:${DB_PORT}:5432`,
    "-e", "POSTGRES_USER=hhapplier", "-e", "POSTGRES_DB=hhapplier", "-e", "POSTGRES_PASSWORD", "postgres:17"],
  { env: { ...process.env, POSTGRES_PASSWORD: dbPassword }, encoding: "utf8" });
  if (run.status !== 0) throw new Error(`Контейнер не запустился: ${redact(run.stderr).slice(0, 400)}`);
}

async function waitDb() {
  // По TCP: при инициализации образ поднимает временный сервер только на сокете
  for (let i = 0; i < 90; i++) {
    if (spawnSync("docker", ["exec", DB_NAME, "pg_isready", "-h", "127.0.0.1", "-U", "hhapplier", "-d", "hhapplier"], { stdio: "ignore" }).status === 0) return;
    await sleep(1000);
  }

  throw new Error("Postgres не поднялась за 90 с");
}

function onApiLine(line) {
  apiLines.push(line);
  if (apiLines.length > 5000) apiLines.shift();
  const sent = /Sending HTTP request (GET|POST) (\S+)/.exec(line);
  if (!sent) return;
  if (/^https?:\/\/([a-z0-9-]+\.)*hh\.ru\//i.test(sent[2])) {
    counters.hh++;
    counters.hhUrls.push(sent[2]);
    if (counters.hh > HH_LIMIT && !hhOver) {
      hhOver = true;
      log(`Больше ${HH_LIMIT} запросов к hh.ru: API останавливается`);
      killApi();
    }
  } else if (sent[1] === "POST" && /chat\/completions/.test(sent[2])) {
    counters.llmHttp++;
  }
}

function startApi() {
  const env = {};
  for (const name of SYSTEM_ENV) if (process.env[name] !== undefined) env[name] = process.env[name];
  Object.assign(env, {
    ASPNETCORE_URLS: `http://${API}`,
    ASPNETCORE_ENVIRONMENT: "Production",
    DOTNET_NOLOGO: "1",
    DOTNET_CLI_TELEMETRY_OPTOUT: "1",
    // Иначе сборка оставит в фоне MSBuild и компилятор, и дерево процессов не очистится
    MSBUILDDISABLENODEREUSE: "1",
    DOTNET_CLI_USE_MSBUILD_SERVER: "0",
    UseSharedCompilation: "false",
    ConnectionStrings__Default: `Host=127.0.0.1;Port=${DB_PORT};Database=hhapplier;Username=hhapplier;Password=${dbPassword}`,
    Admin__Email: adminEmail,
    Admin__Password: adminPassword,
    Hh__Enabled: "true",
    Collector__CollectOnStartup: "false",
    Collector__IntervalMinutes: "10080",
    Collector__MaxPages: "1",
    Collector__MaxConfirmations: "5",
    Llm__ApiKey: llm.LLM_API_KEY,
    Llm__DailyRequestLimit: String(LLM_LIMIT),
    Llm__UserDailyRequestLimit: String(LLM_LIMIT),
    Telegram__BotToken: "",
  });
  if (llm.LLM_BASE_URL) env.Llm__BaseUrl = llm.LLM_BASE_URL;
  if (llm.LLM_MODEL) env.Llm__Model = llm.LLM_MODEL;
  // Без них ключ другого провайдера ушёл бы с настройками по умолчанию
  if (llm.LLM_PROVIDER) env.Llm__Provider = llm.LLM_PROVIDER;
  if (llm.LLM_JSON_MODE) env.Llm__JsonMode = llm.LLM_JSON_MODE;

  const child = spawn("dotnet", ["run", "--project", "server/src/HhAutoApplier.Api", "--no-launch-profile"],
    { cwd: ROOT, env, stdio: ["ignore", "pipe", "pipe"], detached: process.platform !== "win32", windowsHide: true });
  apiChild = child;
  for (const stream of [child.stdout, child.stderr]) {
    stream.setEncoding("utf8");
    let rest = "";
    stream.on("data", (chunk) => {
      const lines = (rest + chunk).split(/\r?\n/);
      rest = lines.pop();
      lines.forEach(onApiLine);
    });
  }

  child.on("exit", () => { if (apiChild === child) apiChild = null; });
}

// --- API и SQL ---

let cookie = null;

// http, а не fetch: у fetch таймаут заголовков 5 минут, а POST /api/collect ждёт конца сбора
function request(method, path, body) {
  return new Promise((done, fail) => {
    const payload = body === undefined ? null : JSON.stringify(body);
    const headers = {};
    if (payload) { headers["Content-Type"] = "application/json"; headers["Content-Length"] = Buffer.byteLength(payload); }
    if (cookie) headers.Cookie = cookie;
    const req = http.request({ host: "127.0.0.1", port: API_PORT, method, path, headers }, (res) => {
      res.setEncoding("utf8");
      let text = "";
      res.on("data", (chunk) => { text += chunk; });
      res.on("end", () => {
        let json = null;
        try { json = text ? JSON.parse(text) : null; } catch { /* не JSON — вернётся текстом */ }
        done({ status: res.statusCode, headers: res.headers, text, json });
      });
    });
    req.on("error", fail);
    if (payload) req.write(payload);
    req.end();
  });
}

async function api(method, path, body) {
  const res = await request(method, path, body);
  if (res.status < 200 || res.status >= 300) throw new Error(`${method} ${path}: ${res.status} ${redact(res.text).slice(0, 400)}`);
  return res.json;
}

async function waitHealth() {
  const deadline = Date.now() + 5 * 60_000;
  while (Date.now() < deadline) {
    if (!apiChild) throw new Error(`API завершился при запуске:\n${redact(apiLines.slice(-30).join("\n"))}`);
    try {
      if ((await request("GET", "/health")).status === 200) return;
    } catch { /* ещё собирается */ }
    await sleep(2000);
  }

  throw new Error("API не ответил на /health за 5 минут");
}

async function login() {
  const res = await request("POST", "/api/auth/login", { email: adminEmail, password: adminPassword });
  if (res.status !== 200) throw new Error(`Вход не удался: ${res.status}`);
  const header = [res.headers["set-cookie"] ?? []].flat().find((c) => c.startsWith("hh_auth="));
  if (!header) throw new Error("В ответе на вход нет cookie");
  cookie = header.split(";")[0];
  secrets.add(cookie.slice("hh_auth=".length));
}

// Запрос через stdin: кавычки идентификаторов и длина не зависят от командной строки ОС
function sql(query) {
  const out = execFileSync("docker", ["exec", "-i", "-e", "PGCLIENTENCODING=UTF8", DB_NAME, "psql", "-U", "hhapplier", "-d", "hhapplier", "-X", "-A", "-t", "-q", "-v", "ON_ERROR_STOP=1", "-f", "-"],
    { input: `select coalesce(json_agg(t), '[]'::json) from (${query}) t;`, encoding: "utf8", maxBuffer: 128 * 1024 * 1024 });
  return JSON.parse(out);
}

const listVacancies = (includeHidden = true) => api("GET", `/api/vacancies?take=500&includeHidden=${includeHidden}`);
const llmUsed = () => sql(`select count(*)::int as n from "LlmCalls" where "Model" <> ''`)[0].n;
const llmExhausted = () => sql(`select count(*)::int as n from "LlmCalls" where "Error" like 'исчерпан%'`)[0].n > 0;

// Письма пишутся тем же проходом сразу после оценки: «Pending нет» ещё не значит, что конвейер закончил
async function waitPipeline(limitMs) {
  const deadline = Date.now() + limitMs;
  let lastCalls = -1;
  let stableSince = Date.now();
  let lastLog = 0;
  while (Date.now() < deadline) {
    const all = await listVacancies();
    const pending = all.filter((v) => v.status === "Pending").length;
    const noLetter = all.filter((v) => (v.status === "Queued" || v.status === "Review") && !v.coverLetter).length;
    const calls = llmUsed();
    if (calls !== lastCalls) { lastCalls = calls; stableSince = Date.now(); }
    if (Date.now() - lastLog > 30_000) { log(`Конвейер: Pending ${pending}, без письма ${noLetter}, ответов LLM ${calls}`); lastLog = Date.now(); }
    if (calls >= LLM_LIMIT || llmExhausted()) return { state: "исчерпан лимит LLM", pending, noLetter };
    if (pending === 0 && (noLetter === 0 || Date.now() - stableSince > SETTLE_MS)) return { state: "завершён", pending, noLetter };
    await sleep(5000);
  }

  const all = await listVacancies();
  return { state: "истекло время ожидания", pending: all.filter((v) => v.status === "Pending").length, noLetter: all.filter((v) => (v.status === "Queued" || v.status === "Review") && !v.coverLetter).length };
}

// --- #71 ---

async function check71() {
  const lines = [];
  const all = await listVacancies();
  const dismissed = all.filter((v) => v.dismissed);
  const primary = dismissed.find((v) => v.llmVerdict === "Reject") ?? dismissed[0];
  if (!primary) return ["Отсеянных карточек нет: #71 не проверен."];

  const inList = async (id, includeHidden) => (await listVacancies(includeHidden)).some((v) => v.id === id);
  const statsJson = async () => JSON.stringify(await api("GET", "/api/stats"));

  lines.push(`Карточка ${primary.id} «${primary.title}»: ${primary.status}, вердикт ${primary.llmVerdict ?? "нет"}, причина: ${primary.statusReason ?? "—"}.`);
  const before = await statsJson();
  const hidden = await api("POST", `/api/vacancies/${primary.id}/hide`);
  lines.push(`hide: hiddenAt ${hidden.hiddenAt ? "задан" : "пуст"}, статус ${hidden.status}; в списке по умолчанию: ${await inList(primary.id, false)}, с includeHidden: ${await inList(primary.id, true)}; /api/stats без изменений: ${before === await statsJson()}.`);
  const shown = await api("POST", `/api/vacancies/${primary.id}/unhide`);
  lines.push(`unhide: hiddenAt ${shown.hiddenAt ? "задан" : "пуст"}, статус ${shown.status}; в списке по умолчанию: ${await inList(primary.id, false)}; /api/stats без изменений: ${before === await statsJson()}.`);

  const reevaluate = async (card, wait) => {
    const after = await api("POST", `/api/vacancies/${card.id}/reevaluate`);
    lines.push(`reevaluate ${card.id}: сразу после запроса ${after.status}, причина: ${after.statusReason ?? "—"}, вердикт ${after.llmVerdict ?? "нет"}.`);
    if (!wait) return after;
    const result = await waitPipeline(REEVALUATE_WAIT_MS);
    const final = (await listVacancies()).find((v) => v.id === card.id);
    lines.push(`Конвейер после reevaluate: ${result.state}; итог ${final?.status}, причина: ${final?.statusReason ?? "—"}, вердикт ${final?.llmVerdict ?? "нет"}, балл ${final?.llmScore ?? "нет"}, письмо ${final?.coverLetter?.length ?? 0} зн.`);
    lines.push(`История: ${(final?.history ?? []).map((h) => h.to).join(" -> ")}; последняя причина: ${final?.history?.at(-1)?.reason ?? "—"}.`);
    return final;
  };

  // Правила могут вернуть карточку только с LLM-вердиктом; при отсеве правилами LLM не тратится
  if (primary.llmVerdict) {
    if (llmUsed() <= LLM_LIMIT - 3) await reevaluate(primary, true);
    else lines.push("reevaluate не выполнен: мало осталось запросов LLM.");
  } else {
    await reevaluate(primary, false);
  }

  const second = dismissed.find((v) => v.id !== primary.id && !v.llmVerdict);
  if (second) {
    lines.push(`Карточка ${second.id} «${second.title}» (отсеяна правилами, LLM не нужна): ${second.status}, причина: ${second.statusReason ?? "—"}.`);
    await reevaluate(second, false);
  }

  return lines;
}

// --- отчёт ---

const cell = (value) => String(value ?? "—").replace(/\|/g, "\\|").replace(/\r?\n/g, " ");
const row = (...cells) => `| ${cells.map(cell).join(" | ")} |`;
const firstUser = (prompt) => {
  const start = prompt.indexOf("[user]\n");
  if (start < 0) return "";
  const rest = prompt.slice(start + "[user]\n".length);
  const end = rest.indexOf("\n\n[assistant]\n");
  return end < 0 ? rest : rest.slice(0, end);
};
const countOf = (text, what) => text.split(what).length - 1;

function promptChecks(calls, descriptions) {
  const byPurpose = new Map();
  for (const call of calls) {
    const stat = byPurpose.get(call.purpose) ?? { total: 0, block: 0, inside: 0, outside: 0, unknown: 0 };
    byPurpose.set(call.purpose, stat);
    stat.total++;
    const user = firstUser(call.prompt ?? "");
    const start = user.indexOf(START);
    const end = user.indexOf(END);
    if (countOf(user, START) === 1 && countOf(user, END) === 1 && start < end) stat.block++;
    const description = (descriptions.get(call.vacancyId) ?? "").slice(0, 60).replace(/<<</g, "«").replace(/>>>/g, "»");
    if (!description) { stat.unknown++; continue; }
    const at = user.indexOf(description);
    if (at > start && at < end) stat.inside++;
    if (at >= 0 && (at < start || at > end)) stat.outside++;
  }

  return byPurpose;
}

const LETTER_FLAGS = [
  ["квадратные скобки", /\[[^\]\n]{1,100}\]/],
  ["ссылка", /https?:\/\/|www\./i],
  ["почта", /[\w.+-]+@[\w-]+\.\w+/],
  ["телефон", /\+?\d[\d\s()-]{9,}\d/],
  ["кодовое слово", /кодов\w+\s+слов/i],
  ["подпись-заглушка", /ваше\s+имя|\(без подписи\)|\(контакты/i],
];

function buildReport({ failure, collect, pipeline, check, vacancies, calls, descriptions, summary }) {
  const out = [];
  out.push("# Живой прогон real-check", "");
  const sha = spawnSync("git", ["rev-parse", "HEAD"], { cwd: ROOT, encoding: "utf8" }).stdout?.trim();
  out.push(`Дата: ${new Date().toISOString()}. Коммит: ${sha}.`, `Поиск: \`${args.query}\`. Импорт: ${ids.join(", ") || "нет"}.`, "");
  if (failure) out.push(`**Прогон прерван:** ${failure}`, "");
  out.push("## Расход", "",
    `- Запросов к hh.ru (по журналу API, без внутренних редиректов): ${counters.hh}`,
    `- Запросов к LLM по журналу API (с повторами HTTP): ${counters.llmHttp}`,
    `- Записей LlmCalls: ${calls.length}, из них с ответом модели: ${calls.filter((c) => c.model).length}`,
    `- Итог сбора: ${collect ? redact(JSON.stringify(collect)) : "нет"}`,
    `- Конвейер: ${pipeline ? JSON.stringify(pipeline) : "нет"}`, "");

  out.push("## Вакансии", "", row("id", "название", "опыт", "статус", "причина статуса", "вердикт", "балл LLM", "письмо, зн.", "причина LLM"), "|---|---|---|---|---|---|---|---|---|");
  for (const v of vacancies)
    out.push(row(v.id, v.title, v.workExperience, v.status, v.statusReason, v.llmVerdict, v.llmScore, v.coverLetter?.length, v.llmReason));
  out.push("", "## LlmCalls по назначению и ошибке", "", row("назначение", "ошибка", "записей", "токены запроса", "токены ответа", "пустых ответов"), "|---|---|---|---|---|---|");
  for (const s of summary) out.push(row(s.purpose, s.error ?? "нет", s.n, s.prompt_tokens, s.completion_tokens, s.empty));

  out.push("", "## Блок данных работодателя в промптах", "", row("назначение", "записей", "блок один и в порядке", "описание внутри блока", "описание вне блока", "описание неизвестно"), "|---|---|---|---|---|---|");
  for (const [purpose, s] of promptChecks(calls, descriptions)) out.push(row(purpose, s.total, s.block, s.inside, s.outside, s.unknown));

  out.push("", "## Просьбы работодателя к письму", "", row("id", "состояние", "тип", "значение", "ответ", "закрыта", "цитата"), "|---|---|---|---|---|---|---|");
  for (const v of vacancies) {
    if (!v.letterRequests?.length) { out.push(row(v.id, v.letterRequestsState, "", "", "", "", "")); continue; }
    for (const r of v.letterRequests) out.push(row(v.id, v.letterRequestsState, r.type, r.value, r.answer, r.closed, r.quote));
  }

  out.push("", "## Признаки в письмах", "");
  const flags = [];
  for (const v of vacancies.filter((x) => x.coverLetter))
    for (const [name, re] of LETTER_FLAGS) {
      const match = re.exec(v.coverLetter);
      if (match) flags.push(`- ${v.id}: ${name}: \`${match[0]}\``);
    }
  out.push(...(flags.length ? flags : ["Нет."]));

  out.push("", "## #71: скрыть, вернуть, оценить заново", "", ...(check ?? ["Не выполнено."]).map((l) => `- ${l}`));

  const bad = calls.filter((c) => c.error || !c.response?.trim());
  out.push("", "## Неразобранные, пустые и отклонённые ответы", "");
  if (!bad.length) out.push("Нет.");
  for (const c of bad) out.push(`- #${c.id} ${c.purpose} ${c.vacancyId ?? ""}: ${c.error ?? "пустой ответ"}; ответ ${c.response?.length ?? 0} зн.: ${(c.response ?? "").slice(0, 300).replace(/\s+/g, " ")}`);

  const warnings = [];
  apiLines.forEach((line, i) => {
    if (/^(warn|fail|crit):/.test(line)) warnings.push(line, apiLines[i + 1] ?? "");
  });
  out.push("", "## Предупреждения журнала API", "", "```", ...(warnings.length ? warnings.slice(-80) : ["нет"]), "```");

  out.push("", "## Письма", "");
  for (const v of vacancies.filter((x) => x.coverLetter)) out.push(`### ${v.id}: ${v.title}`, "", v.coverLetter, "");

  out.push("", "## Журнал LlmCalls: ответы", "");
  for (const c of calls) out.push(`### #${c.id} ${c.purpose} ${c.vacancyId ?? ""}${c.error ? ` (ошибка: ${c.error})` : ""}`, "", "```", (c.response ?? "").slice(0, 1500), "```", "");

  out.push("## Пример промпта по каждому назначению", "");
  const seen = new Set();
  for (const c of calls) {
    if (seen.has(c.purpose) || !c.prompt) continue;
    seen.add(c.purpose);
    out.push(`### ${c.purpose}, вакансия ${c.vacancyId ?? "—"}`, "", "```", c.prompt, "```", "");
  }

  return redact(out.join("\n"));
}

// --- основной ход ---

async function report(state) {
  const summary = sql(`select "Purpose" as purpose, "Error" as error, count(*)::int as n, sum("PromptTokens")::int as prompt_tokens, sum("CompletionTokens")::int as completion_tokens,
    count(*) filter (where coalesce(btrim("Response"), '') = '')::int as empty from "LlmCalls" group by 1, 2 order by 1, 2`);
  const calls = sql(`select "Id" as id, "Purpose" as purpose, "VacancyId" as "vacancyId", "Model" as model, "Prompt" as prompt, "Response" as response, "Error" as error from "LlmCalls" order by "Id"`);
  const descriptions = new Map(sql(`select "Id" as id, "Description" as description from "Vacancies"`).map((v) => [v.id, v.description]));
  let vacancies = [];
  try { vacancies = await listVacancies(); } catch (e) { state.failure = `${state.failure ?? ""} Список вакансий недоступен: ${e.message}`; }
  // Импортные — первыми: это контрольные примеры
  vacancies.sort((a, b) => Number(ids.includes(b.id)) - Number(ids.includes(a.id)) || a.id.localeCompare(b.id));
  const text = buildReport({ ...state, vacancies, calls, descriptions, summary });
  const path = resolve(args.report);
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, `${text}\n`, "utf8");
  log(`Отчёт: ${path}`);
  return { vacancies: vacancies.length, calls: calls.length };
}

async function main() {
  const state = {};
  for (const port of [DB_PORT, API_PORT]) if (!(await portFree(port))) throw new Error(`Порт ${port} занят`);
  log("Запуск Postgres");
  startDb();
  await waitDb();
  log("Запуск API (сборка может занять пару минут)");
  startApi();
  await waitHealth();
  await login();

  try {
    await api("PUT", "/api/resumes", RESUME);
    // Вымышленные значения: просьбы работодателя (#72) закрываются только из профиля
    await api("PUT", "/api/profile", { general: "", skills: [], desiredSalaryRub: 150000, readyForTestTask: true });
    await api("PUT", "/api/searches", { name: SEARCH, query: args.query, enabled: true, rules: { levelKeywords: LEVEL_KEYWORDS } });
    if (ids.length) {
      const imported = await api("POST", "/api/import", { search: SEARCH, vacancies: ids.map((id) => ({ id, title: `Вакансия ${id}`, url: `https://hh.ru/vacancy/${id}` })) });
      log(`Импорт: ${JSON.stringify(imported)}`);
    }

    log("Сбор: POST /api/collect");
    const collected = await request("POST", "/api/collect");
    state.collect = collected.json ?? collected.text;
    log(`Сбор: ${collected.status} ${JSON.stringify(state.collect)}`);
    if (collected.status === 502) state.failure = "источник заблокировал сбор, повторов не было";
    else if (collected.status !== 200) throw new Error(`POST /api/collect: ${collected.status}`);

    state.pipeline = await waitPipeline(PIPELINE_WAIT_MS);
    log(`Конвейер: ${JSON.stringify(state.pipeline)}`);
    state.check = await check71();
  } catch (e) {
    state.failure = hhOver ? `остановлено предохранителем: больше ${HH_LIMIT} запросов к hh.ru` : redact(e.message);
    process.exitCode = 1;
    log(`Ошибка: ${state.failure}`);
  }

  const written = await report(state);
  log(`Итог: вакансий ${written.vacancies}, записей LlmCalls ${written.calls}, запросов hh.ru ${counters.hh}, запросов LLM ${counters.llmHttp}`);
}

try {
  await main();
} catch (e) {
  process.exitCode = 1;
  console.error(`[real-check] ${redact(e.stack ?? e)}`);
} finally {
  cleanup();
  await sleep(1000);
  for (const port of [DB_PORT, API_PORT]) if (!(await portFree(port))) { process.exitCode = 1; console.error(`[real-check] Порт ${port} всё ещё занят`); }
}
