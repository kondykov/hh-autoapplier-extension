import { spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";

// Статусы задач в GitHub Project и CI по коммиту — обёртка над gh (нужен `gh auth login` со scope project).
// Правила статусов — AGENTS.md (Git Workflow), порядок действий — .claude/skills/task-status/SKILL.md.
const USAGE = `node scripts/gh-task.mjs <команда> [параметры]

  show N                          issue и его поля в проекте
  status N <Статус> [--comment T] сменить Status (добавит issue в проект); Статус: Backlog|Todo|"In Progress"|Waiting|Done
  done N --comment T [--start D] [--end D]
                                  комментарий, закрытие issue, Done, даты «Начало»/«Срок»
                                  (по умолчанию: Начало — уже стоящее или дата первого коммита с #N, Срок — сегодня)
  board [Статус...]               задачи по статусам (по умолчанию Todo, In Progress, Waiting)
  ci [sha] [--wait] [--timeout S] запуски CI по коммиту (по умолчанию HEAD); --wait — ждать завершения
                                  код выхода: 0 — зелёный, 1 — упал, 2 — запуска нет или не дождались

  Общие: --project N (по умолчанию 5), --owner LOGIN (kondykov), --repo owner/name (из git remote origin)
  Текст комментария: --comment "..." или --comment-file путь (- — stdin).`;

// ---------- аргументы ----------
const argv = process.argv.slice(2);
const flags = {};
const pos = [];
for (let i = 0; i < argv.length; i++) {
  const a = argv[i];
  if (a === "--wait") flags.wait = true;
  else if (a.startsWith("--")) flags[a.slice(2)] = argv[++i];
  else pos.push(a);
}
const [cmd, ...rest] = pos;
if (!cmd || cmd === "help" || flags.help !== undefined) {
  console.log(USAGE);
  process.exit(cmd ? 0 : 1);
}

const GH = process.env.GH_BIN || "gh";
const OWNER = flags.owner || "kondykov";
const PROJECT = Number(flags.project || 5);
const REPO = flags.repo || repoFromRemote();
const today = () => new Date().toLocaleDateString("sv-SE"); // YYYY-MM-DD по местному времени

function fail(msg, code = 1) {
  console.error(msg);
  process.exit(code);
}

function run(bin, args, input) {
  const r = spawnSync(bin, args, { encoding: "utf8", input, maxBuffer: 64 * 1024 * 1024 });
  if (r.error) fail(`${bin}: ${r.error.message}`);
  return r;
}

function gh(args, input) {
  const r = run(GH, args, input);
  if (r.status !== 0) fail(`gh ${args.slice(0, 3).join(" ")}: ${(r.stderr || r.stdout).trim()}`);
  return r.stdout;
}

function repoFromRemote() {
  const url = run("git", ["remote", "get-url", "origin"]).stdout.trim();
  const m = url.match(/github\.com[:/]([^/]+\/[^/]+?)(?:\.git)?$/);
  if (!m) fail(`Не понял репозиторий из origin (${url}); укажите --repo owner/name`);
  return m[1];
}

// GraphQL через gh api: переменные строками (-f), числа (-F)
function graphql(query, vars = {}) {
  const args = ["api", "graphql", "-f", `query=${query}`];
  for (const [k, v] of Object.entries(vars)) args.push(typeof v === "number" ? "-F" : "-f", `${k}=${v}`);
  const out = JSON.parse(gh(args));
  if (out.errors) fail(out.errors.map((e) => e.message).join("\n"));
  return out.data;
}

// ---------- проект ----------
let projectCache;
function project() {
  if (projectCache) return projectCache;
  const d = graphql(
    `query($login:String!,$n:Int!){user(login:$login){projectV2(number:$n){id title fields(first:50){nodes{
      ... on ProjectV2FieldCommon{id name dataType}
      ... on ProjectV2SingleSelectField{options{id name}}}}}}}`,
    { login: OWNER, n: PROJECT },
  );
  const p = d.user?.projectV2;
  if (!p) fail(`Проект ${OWNER}/${PROJECT} не найден`);
  const fields = Object.fromEntries(p.fields.nodes.filter((f) => f.name).map((f) => [f.name, f]));
  return (projectCache = { id: p.id, title: p.title, fields });
}

function field(name) {
  const f = project().fields[name];
  if (!f) fail(`В проекте «${project().title}» нет поля «${name}»`);
  return f;
}

function optionId(fieldName, value) {
  const f = field(fieldName);
  const o = f.options?.find((x) => x.name.toLowerCase() === value.toLowerCase());
  if (!o) fail(`У поля «${fieldName}» нет варианта «${value}»; есть: ${f.options?.map((x) => x.name).join(", ")}`);
  return o.id;
}

const ISSUE_QUERY = `query($owner:String!,$repo:String!,$n:Int!){repository(owner:$owner,name:$repo){issue(number:$n){
  id number title state url labels(first:20){nodes{name}}
  projectItems(first:20){nodes{id project{id}
    fieldValues(first:30){nodes{
      ... on ProjectV2ItemFieldSingleSelectValue{name field{... on ProjectV2FieldCommon{name}}}
      ... on ProjectV2ItemFieldDateValue{date field{... on ProjectV2FieldCommon{name}}}
      ... on ProjectV2ItemFieldTextValue{text field{... on ProjectV2FieldCommon{name}}}}}}}}}}`;

function issue(n) {
  const [owner, repo] = REPO.split("/");
  const i = graphql(ISSUE_QUERY, { owner, repo, n: Number(n) }).repository?.issue;
  if (!i) fail(`Issue ${REPO}#${n} не найден`);
  const item = i.projectItems.nodes.find((x) => x.project.id === project().id);
  const values = {};
  for (const v of item?.fieldValues.nodes ?? []) if (v.field) values[v.field.name] = v.name ?? v.date ?? v.text;
  return { ...i, labels: i.labels.nodes.map((l) => l.name), itemId: item?.id, values };
}

function ensureItem(i) {
  if (i.itemId) return i.itemId;
  const d = graphql(
    `mutation($p:ID!,$c:ID!){addProjectV2ItemById(input:{projectId:$p,contentId:$c}){item{id}}}`,
    { p: project().id, c: i.id },
  );
  console.log(`#${i.number} добавлен в проект «${project().title}»`);
  return d.addProjectV2ItemById.item.id;
}

function setValue(itemId, fieldName, value) {
  const f = field(fieldName);
  const isDate = f.dataType === "DATE";
  const valueType = isDate ? "Date!" : "String!";
  const valueExpr = isDate ? "{date:$v}" : "{singleSelectOptionId:$v}";
  graphql(
    `mutation($p:ID!,$i:ID!,$f:ID!,$v:${valueType}){updateProjectV2ItemFieldValue(input:{projectId:$p,itemId:$i,fieldId:$f,value:${valueExpr}}){projectV2Item{id}}}`,
    { p: project().id, i: itemId, f: f.id, v: isDate ? value : optionId(fieldName, value) },
  );
}

// Часть изменений молча не применяется: перечитываем и повторяем
function setVerified(n, changes) {
  let i = issue(n);
  const itemId = ensureItem(i);
  for (let attempt = 1; attempt <= 3; attempt++) {
    const pending = Object.entries(changes).filter(([k, v]) => (i.values[k] ?? "").toLowerCase() !== v.toLowerCase());
    if (!pending.length) return i;
    for (const [k, v] of pending) setValue(itemId, k, v);
    i = issue(n);
  }
  const left = Object.entries(changes).filter(([k, v]) => (i.values[k] ?? "").toLowerCase() !== v.toLowerCase());
  if (left.length) fail(`#${n}: не применилось после 3 попыток: ${left.map(([k, v]) => `${k}=${v}`).join(", ")}`);
  return i;
}

function commentText() {
  if (flags["comment-file"] !== undefined) {
    const f = flags["comment-file"];
    return readFileSync(f === "-" ? 0 : f, "utf8").trim();
  }
  return flags.comment?.trim();
}

function addComment(n, text) {
  gh(["issue", "comment", String(n), "--repo", REPO, "--body-file", "-"], text);
}

function firstCommitDate(n) {
  const r = run("git", ["log", "--all", "--reverse", "-E", `--grep=#${n}([^0-9]|$)`, "--format=%cs"]);
  return r.stdout.split("\n").find(Boolean);
}

function printIssue(i) {
  const v = i.values;
  console.log(`#${i.number} [${i.state}] ${i.title}`);
  console.log(`  ${i.url}`);
  if (!i.itemId) return console.log(`  не в проекте «${project().title}»`);
  for (const [k, val] of Object.entries(v)) if (k !== "Title") console.log(`  ${k}: ${val}`);
  if (i.labels.length) console.log(`  Метки: ${i.labels.join(", ")}`);
}

const isDate = (s) => /^\d{4}-\d{2}-\d{2}$/.test(s);

// ---------- команды ----------
const commands = {
  show() {
    if (!rest[0]) fail(USAGE);
    printIssue(issue(rest[0]));
  },

  status() {
    const [n, ...statusParts] = rest;
    const status = statusParts.join(" ");
    if (!n || !status) fail(USAGE);
    if (status.toLowerCase() === "done") fail("Для Done — команда done (закрытие, комментарий, даты)");
    optionId("Status", status);
    const text = commentText();
    if (text) addComment(n, text);
    const i = setVerified(n, { Status: status });
    console.log(`#${n} → ${i.values.Status}${text ? " (комментарий добавлен)" : ""}`);
  },

  done() {
    const n = rest[0];
    const text = commentText();
    if (!n) fail(USAGE);
    if (!text) fail("done требует --comment или --comment-file: коммит, что проверено и что нет");
    let i = issue(n);
    const start = flags.start || i.values["Начало"] || firstCommitDate(n) || today();
    const end = flags.end || today();
    if (!isDate(start) || !isDate(end)) fail("Даты — в формате YYYY-MM-DD");
    if (i.state === "OPEN") {
      addComment(n, text);
      gh(["issue", "close", String(n), "--repo", REPO]);
    } else {
      console.log(`#${n} уже закрыт — комментарий не добавляю`);
    }
    i = setVerified(n, { Status: "Done", "Начало": start, "Срок": end });
    console.log(`#${n} закрыт → Done, Начало ${i.values["Начало"]}, Срок ${i.values["Срок"]}`);
  },

  board() {
    const wanted = rest.length ? rest : ["Todo", "In Progress", "Waiting"];
    const items = [];
    let after = null;
    do {
      const d = graphql(
        `query($login:String!,$n:Int!,$after:String){user(login:$login){projectV2(number:$n){items(first:100,after:$after){
          pageInfo{hasNextPage endCursor}
          nodes{content{... on Issue{number title state} ... on PullRequest{number title state}}
            fieldValueByName(name:"Status"){... on ProjectV2ItemFieldSingleSelectValue{name}}}}}}}`,
        after ? { login: OWNER, n: PROJECT, after } : { login: OWNER, n: PROJECT },
      );
      const page = d.user.projectV2.items;
      items.push(...page.nodes);
      after = page.pageInfo.hasNextPage ? page.pageInfo.endCursor : null;
    } while (after);
    for (const s of wanted) {
      const list = items.filter((x) => (x.fieldValueByName?.name ?? "").toLowerCase() === s.toLowerCase() && x.content?.number);
      console.log(`${s} (${list.length})`);
      for (const x of list.sort((a, b) => a.content.number - b.content.number))
        console.log(`  #${x.content.number} ${x.content.state === "CLOSED" ? "[закрыт] " : ""}${x.content.title}`);
    }
  },

  async ci() {
    const ref = rest[0] || "HEAD";
    const sha = run("git", ["rev-parse", ref]).stdout.trim();
    if (!sha) fail(`Не нашёл коммит ${ref}`);
    const timeout = Number(flags.timeout || 900) * 1000;
    const started = Date.now();
    let runs = [];
    for (;;) {
      runs = JSON.parse(
        gh(["run", "list", "--repo", REPO, "--commit", sha, "--json", "databaseId,workflowName,status,conclusion,url", "-L", "20"]),
      );
      const pending = !runs.length || runs.some((r) => r.status !== "completed");
      if (!flags.wait || !pending || Date.now() - started > timeout) break;
      await new Promise((r) => setTimeout(r, 15000));
    }
    console.log(`Коммит ${sha.slice(0, 7)}:`);
    if (!runs.length) {
      console.log("  запусков нет (ещё не создан или GitHub пропустил — через ~3 мин без запуска нужен пустой коммит)");
      process.exit(2);
    }
    for (const r of runs) console.log(`  ${r.workflowName}: ${r.status}${r.conclusion ? ` / ${r.conclusion}` : ""}  ${r.url}`);
    const failed = runs.filter((r) => r.status === "completed" && !["success", "skipped", "neutral"].includes(r.conclusion));
    for (const r of failed) {
      const log = run(GH, ["run", "view", String(r.databaseId), "--repo", REPO, "--log-failed"]).stdout.split("\n");
      console.log(`\n--- ${r.workflowName}: последние строки упавших шагов ---`);
      console.log(log.slice(-60).join("\n"));
    }
    if (failed.length) process.exit(1);
    if (runs.some((r) => r.status !== "completed")) process.exit(2);
  },
};

if (!commands[cmd]) fail(`Неизвестная команда ${cmd}\n\n${USAGE}`);
await commands[cmd]();
