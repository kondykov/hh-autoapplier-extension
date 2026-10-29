import { chromium } from "playwright";

// Read-only осмотр запущенного dev-браузера (scripts/dev-browser.mjs) через CDP :9222.
// Печатает вкладки, флаги очереди и пробного режима; с --form ещё снимок формы отклика на вкладке hh.ru.
// Ничего не нажимает и не закрывает.
const withForm = process.argv.includes("--form");

const browser = await chromium.connectOverCDP("http://127.0.0.1:9222").catch(() => null);
if (!browser) {
  console.log("dev-браузер не запущен (CDP :9222 не отвечает)");
  process.exit(1);
}
const ctx = browser.contexts()[0];

console.log("вкладки:");
for (const p of ctx.pages()) console.log(" -", p.url(), "|", await p.title().catch(() => ""));

const sw = ctx.serviceWorkers()[0] ?? (await ctx.waitForEvent("serviceworker", { timeout: 10000 }).catch(() => null));
if (sw) {
  const flags = await sw.evaluate(async () => {
    const all = await chrome.storage.local.get(null);
    const key = Object.keys(all).find((k) => all[k] && typeof all[k] === "object" && "queueRunning" in all[k]);
    return { queueRunning: all[key]?.queueRunning, dryRun: all.hh_autoapply_config?.queueDryRun };
  });
  console.log("флаги:", JSON.stringify(flags));
} else console.log("флаги: service worker расширения не найден");

if (withForm) {
  const page = ctx.pages().find((p) => p.url().includes("hh.ru"));
  const form = await page?.evaluate(() => ({
    url: location.href,
    resume: document.querySelector('[data-qa="resume-title"]')?.textContent.trim().slice(0, 60),
    questions: [...document.querySelectorAll('[data-qa="task-body"]')].map((b) =>
      b.querySelector('[data-qa="task-question"]')?.textContent.replace(/\s+/g, " ").trim().slice(0, 90)),
    letter: [...document.querySelectorAll("textarea")]
      .find((t) => !t.closest('[data-qa="task-body"]') && !t.closest("#hh-autoapply-panel"))?.value.slice(0, 200),
  }));
  console.log("форма:", JSON.stringify(form ?? "вкладки hh.ru нет"));
}
// Без browser.close(): он закрыл бы dev-браузер владельца
process.exit(0);
