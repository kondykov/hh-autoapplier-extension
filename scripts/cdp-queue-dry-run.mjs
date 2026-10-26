import { chromium } from "playwright";

// Один проход режима «из очереди» в пробном режиме через CDP запущенного dev-браузера (scripts/dev-browser.mjs).
// Отказывается работать, если пробный режим выключен или очередь уже идёт.
// Печатает журнал панели и снимок формы (резюме, анкета, письмо) на каждом «готов к отправке».
// «Отправить» не нажимает расширение; скрипт сам ничего на hh.ru не нажимает, кроме кнопки панели «Из очереди».
const TIMEOUT_MS = 6 * 60 * 1000;

const browser = await chromium.connectOverCDP("http://127.0.0.1:9222");
const ctx = browser.contexts()[0];
const sw = ctx.serviceWorkers()[0] ?? (await ctx.waitForEvent("serviceworker", { timeout: 20000 }));

const flags = () =>
  sw.evaluate(async () => {
    const all = await chrome.storage.local.get(null);
    const key = Object.keys(all).find((k) => all[k] && typeof all[k] === "object" && "queueRunning" in all[k]);
    return { queueRunning: all[key]?.queueRunning, dryRun: all.hh_autoapply_config?.queueDryRun };
  });

const start = await flags();
console.log("флаги:", JSON.stringify(start));
if (start.queueRunning || start.dryRun !== true) {
  console.log("Отмена: нужен пробный режим и выключенная очередь");
  process.exit(1);
}

const snapshot = (page) =>
  page.evaluate(() => ({
    url: location.href,
    resume: document.querySelector('[data-qa="resume-title"]')?.textContent.trim().slice(0, 60),
    questions: [...document.querySelectorAll('[data-qa="task-body"]')].map((b) => ({
      q: b.querySelector('[data-qa="task-question"]')?.textContent.replace(/\s+/g, " ").trim().slice(0, 90),
      checked: [...b.querySelectorAll("input:checked")].map((i) => i.closest("label")?.textContent.replace(/\s+/g, " ").trim()),
      text: [...b.querySelectorAll("textarea")].map((t) => t.value),
    })),
    // Поле письма — textarea вне вопросов анкеты и вне панели расширения
    letter: [...document.querySelectorAll("textarea")]
      .find((t) => !t.closest('[data-qa="task-body"]') && !t.closest("#hh-autoapply-panel"))?.value.slice(0, 200),
  }));

const watch = (page) =>
  page.on("console", async (m) => {
    const text = m.text();
    if (!text.includes("[HH-AutoApply]")) return;
    console.log(" >", text.replace(/%c\[HH-AutoApply\] /, "").replace(/ color:#[0-9A-F]+;font-weight:bold$/i, ""));
    if (text.includes("готов к отправке")) console.log("   форма:", JSON.stringify(await snapshot(page).catch((e) => e.message)));
  });
ctx.pages().forEach(watch);
ctx.on("page", watch);

const page = ctx.pages().find((p) => p.url().includes("hh.ru")) ?? ctx.pages()[0];
await page.waitForSelector("#hh-btn-queue", { state: "attached", timeout: 30000 });
await page.evaluate(() => document.getElementById("hh-btn-queue").click());

const began = Date.now();
await new Promise((r) => setTimeout(r, 3000));
while (Date.now() - began < TIMEOUT_MS && (await flags().catch(() => ({ queueRunning: true }))).queueRunning)
  await new Promise((r) => setTimeout(r, 2000));
console.log("итог:", JSON.stringify(await flags()));
// Без browser.close(): он закрыл бы dev-браузер владельца
process.exit(0);
