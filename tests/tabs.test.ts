import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { BackendClient } from "../src/api/client";
import { loadConfig } from "../src/core/config";
import { Controller } from "../src/core/controller";
import { DailyLimit } from "../src/core/daily-limit";
import { StateRepository } from "../src/core/state";
import { Logger } from "../src/infra/logger";
import { MemoryStorage } from "../src/infra/storage";
import { StaticTemplateProvider } from "../src/letters/letter-provider";
import type { QueueItem } from "../src/queue/queue-mode";
import { ResumeSelector } from "../src/resume/selector";
import { Panel, type PanelHandlers } from "../src/ui/panel";

// #51: две вкладки hh.ru над одним chrome.storage — очередь ведёт одна, остановка и настройки доходят до неё сразу
const item = (id: string): QueueItem => ({ id, title: `Вакансия ${id}`, company: "Acme", resume: null });
const handlers = new Proxy({}, { get: () => () => {} }) as PanelHandlers;

async function tab(storage: MemoryStorage, self: number, opts: { open?: (id: number) => boolean } = {}) {
  const config = await loadConfig(storage);
  const repo = new StateRepository(storage);
  const state = await repo.load();
  const logger = new Logger();
  const messages: string[] = [];
  logger.addSink({ write: (m) => messages.push(m) });
  const panel = new Panel(storage, handlers, {
    limit: config.dailyLimit, queueRunLimit: config.queueRunLimit, letter: "", server: config.server,
    importSearch: "", queueDryRun: config.queueDryRun, rejectionReply: config.rejectionReply,
  });
  await panel.mount();
  const visited: string[] = [];
  const backend = {
    getServerVersion: vi.fn(async () => ({ state: "unknown" as const })),
    getQueue: vi.fn(async () => ({ ok: true as const, items: [item("1"), item("2")] })),
    getResumes: vi.fn(async () => ({ ok: true as const, resumes: [] })),
    reportStatus: vi.fn(async () => ({ ok: true as const })),
    reportEvent: vi.fn(async () => ({ ok: true as const })),
  } as unknown as BackendClient;
  const controller = new Controller({
    storage, config, state, repo, logger, panel, backend,
    limit: new DailyLimit(storage, config.dailyLimit),
    letters: new StaticTemplateProvider(() => ""),
    resumes: new ResumeSelector(logger),
    nav: { url: () => "https://hh.ru/", go: (to) => visited.push(to) },
    tab: { self, isOpen: async (id) => opts.open?.(id) ?? true },
  });
  // init вкладки-владельца сразу открывает вакансию — с паузой перед переходом
  const ready = controller.init();
  await vi.advanceTimersByTimeAsync(20000);
  await ready;
  return { controller, state, config, visited, messages, panel };
}

// CaptchaGuard опрашивает страницу setInterval — все таймеры не прокрутить, только на время
const elapse = () => vi.advanceTimersByTimeAsync(20000);

beforeEach(() => {
  vi.useFakeTimers();
  document.body.innerHTML = "";
});
afterEach(() => vi.useRealTimers());

describe("очередь в нескольких вкладках", () => {
  it("очередь ведёт вкладка, которая её запустила; другая её не трогает", async () => {
    const storage = new MemoryStorage();
    const a = await tab(storage, 1);
    const b = await tab(storage.fork(), 2);

    const started = a.controller.queue.toggle();
    await elapse();
    await started;

    expect(a.visited).toHaveLength(1);
    expect(b.state.queueRunning).toBe(true);
    expect(b.controller.queue.accepts("unknown", b.state)).toBe(false);
    expect(b.panel["el"]("hh-step-desc").textContent).toContain("в другой вкладке");

    // Страница во второй вкладке загрузилась при идущей очереди — не перехватывает её
    const c = await tab(storage.fork(), 3);
    await elapse();
    expect(c.visited).toEqual([]);
    expect(c.messages.join("\n")).not.toContain("не открылась");
  });

  it("«Стоп очередь» в другой вкладке останавливает очередь там, где она идёт", async () => {
    const storage = new MemoryStorage();
    const a = await tab(storage, 1);
    const b = await tab(storage.fork(), 2);
    const started = a.controller.queue.toggle();
    await elapse();
    await started;

    await b.controller.queue.toggle();

    expect(a.state.queueRunning).toBe(false);
    expect(a.messages.at(-1)).toContain("остановлена в другой вкладке");
    expect(b.messages.at(-1)).toContain("Очередь в другой вкладке остановлена");
  });

  it("свой перезапуск не отменяется запоздавшей остановкой прошлого запуска", async () => {
    const storage = new MemoryStorage();
    const a = await tab(storage, 1);
    const b = await tab(storage.fork(), 2);
    const started = a.controller.queue.toggle();
    await elapse();
    await started;
    const firstRun = a.state.queueRunId;

    await a.controller.queue.toggle();
    const again = a.controller.queue.toggle();
    await elapse();
    await again;
    expect(a.state.queueRunId).not.toBe(firstRun);

    // Вторая вкладка записывает состояние со «старой» остановкой — запуск в первой продолжается
    Object.assign(b.state, { queueRunning: false, queueRunId: firstRun, queueTab: 1 });
    await b.controller["persist"]();
    expect(a.state.queueRunning).toBe(true);
  });

  it("пробный режим, снятый или включённый в другой вкладке, действует сразу", async () => {
    const storage = new MemoryStorage();
    const a = await tab(storage, 1);
    const b = await tab(storage.fork(), 2);

    await b.controller.settings.setQueueDryRun(false);
    expect(a.config.queueDryRun).toBe(false);
    expect(a.panel["el"]<HTMLInputElement>("hh-input-dryrun").checked).toBe(false);

    await b.controller.settings.setQueueDryRun(true);
    expect(a.config.queueDryRun).toBe(true);
    expect(a.messages.at(-1)).toContain("Пробный режим включён в другой вкладке");
  });

  it("вкладки-владельца больше нет (браузер перезапущен) — очередь продолжает новая вкладка", async () => {
    const storage = new MemoryStorage();
    await storage.set("hh_autoapply_state", { queueRunning: true, queueTab: 7, queueRunId: "r1", queueTried: [] });

    const alive = await tab(storage.fork(), 2);
    expect(alive.state.queueTab).toBe(7);
    expect(alive.visited).toEqual([]);

    document.body.innerHTML = "";
    const orphanOwner = await tab(storage.fork(), 3, { open: () => false });
    await elapse();
    expect(orphanOwner.state.queueTab).toBe(3);
    expect(orphanOwner.visited).toHaveLength(1);
  });
});

describe("MemoryStorage: события как у chrome.storage.onChanged", () => {
  it("своя запись не приходит своей же вкладке, чужая — приходит", async () => {
    const a = new MemoryStorage();
    const b = a.fork();
    const seenA = vi.fn();
    const seenB = vi.fn();
    a.watch("k", seenA);
    b.watch("k", seenB);

    await a.set("k", { v: 1 });

    expect(seenA).not.toHaveBeenCalled();
    expect(seenB).toHaveBeenCalledWith({ v: 1 });
  });
});
