import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ApplyResult } from "../src/apply/pipeline";
import { DEFAULT_CONFIG, type AppConfig } from "../src/core/config";
import { DailyLimit } from "../src/core/daily-limit";
import { AppState, type Vacancy } from "../src/core/state";
import { Logger } from "../src/infra/logger";
import { MemoryStorage } from "../src/infra/storage";
import { QueueStep } from "../src/pages/queue-step";
import { SearchStep } from "../src/pages/search-step";
import { pickStep, type StepContext } from "../src/pages/step-context";
import { VacancyStep } from "../src/pages/vacancy-step";
import type { QueueItem } from "../src/queue/queue-mode";

const item = (id: string): QueueItem => ({ id, title: `Вакансия ${id}`, company: "Acme", resume: ".NET" });
const RESPONSE = (id: string) => `https://hh.ru/applicant/vacancy_response?vacancyId=${id}`;

// Контекст шага без браузера: навигация, панель, backend и конвейер — подменные
function context(opts: { url?: string; queue?: QueueItem[]; result?: ApplyResult; config?: Partial<AppConfig>; dailyLimit?: number } = {}) {
  let url = opts.url ?? "https://hh.ru/";
  const visited: string[] = [];
  const messages: string[] = [];
  const logger = new Logger();
  logger.addSink({ write: (m) => messages.push(m) });

  const ctx = {
    state: new AppState(),
    config: { ...DEFAULT_CONFIG, ...opts.config },
    storage: new MemoryStorage(),
    logger,
    limit: new DailyLimit(new MemoryStorage(), opts.dailyLimit ?? 10),
    panel: {
      setStep: vi.fn(),
      setNextEnabled: vi.fn(),
      setAutoActive: vi.fn(),
      setQueueActive: vi.fn(),
      setRejectActive: vi.fn(),
      setUrl: vi.fn(),
    },
    backend: {
      getQueue: vi.fn(async () => ({ ok: true as const, items: opts.queue ?? [] })),
      reportStatus: vi.fn(async () => ({ ok: true as const })),
    },
    pipeline: {
      applyOnPage: vi.fn(async (_v: Vacancy) => opts.result ?? "success"),
      applyFromSearch: vi.fn(async (_v: Vacancy) => opts.result ?? "success"),
    },
    captcha: { waitUntilClear: vi.fn(async () => false), consumeHit: vi.fn(() => false) },
    nav: {
      url: () => url,
      go: vi.fn((to: string) => {
        visited.push(to);
        url = to;
      }),
      reload: vi.fn(),
    },
    persist: vi.fn(async () => {}),
  } satisfies StepContext;
  return { ctx, visited, messages, setUrl: (u: string) => (url = u) };
}

const run = async (promise: Promise<unknown>) => {
  await vi.runAllTimersAsync();
  await promise;
};

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

describe("pickStep", () => {
  it("очередь важнее авто-режима, авто-режим — по типу страницы, без режимов — никто", () => {
    const { ctx } = context();
    const steps = [new QueueStep(ctx), new SearchStep(ctx), new VacancyStep(ctx)];
    const state = new AppState();

    expect(pickStep(steps, "search", state)).toBeNull();

    state.running = true;
    expect(pickStep(steps, "search", state)?.name).toBe("выдача");
    expect(pickStep(steps, "response", state)?.name).toBe("страница вакансии");
    expect(pickStep(steps, "unknown", state)).toBeNull();

    state.queueRunning = true;
    expect(pickStep(steps, "search", state)?.name).toBe("очередь");
    expect(pickStep(steps, "unknown", state)?.name).toBe("очередь");
  });
});

describe("QueueStep", () => {
  it("запуск: выключает авто-режим и открывает страницу отклика первой вакансии", async () => {
    const { ctx, visited } = context({ queue: [item("1"), item("2")] });
    ctx.state.running = true;

    await run(new QueueStep(ctx).toggle());

    expect(ctx.state.running).toBe(false);
    expect(ctx.state.queueRunning).toBe(true);
    expect(ctx.state.queueCurrent?.id).toBe("1");
    expect(visited).toEqual([RESPONSE("1")]);
  });

  it("пустая очередь и недоступный сервер останавливают режим", async () => {
    const empty = context({ queue: [] });
    await run(new QueueStep(empty.ctx).toggle());
    expect(empty.ctx.state.queueRunning).toBe(false);
    expect(empty.messages.at(-1)).toContain("Очередь пуста");

    const down = context();
    down.ctx.backend.getQueue.mockResolvedValue({ ok: false, error: "сервер недоступен: timeout" } as never);
    await run(new QueueStep(down.ctx).toggle());
    expect(down.ctx.state.queueRunning).toBe(false);
    expect(down.messages.at(-1)).toContain("сервер недоступен");
  });

  it("на странице отклика: исход уходит на сервер, затем следующая вакансия", async () => {
    const { ctx, visited } = context({ url: RESPONSE("1"), queue: [item("1"), item("2")], result: "success", config: { queueDryRun: false } });
    Object.assign(ctx.state, { queueRunning: true, queueCurrent: { ...item("1"), letter: "Письмо под вакансию" }, queueTried: ["1"] });

    await run(new QueueStep(ctx).resume());

    const v = ctx.pipeline.applyOnPage.mock.calls[0][0];
    expect(v.resume).toBe(".NET");
    expect(v.letter).toBe("Письмо под вакансию");
    expect(ctx.backend.reportStatus).toHaveBeenCalledWith("1", { status: "Applied", reason: null }, ".NET");
    expect(visited).toEqual([RESPONSE("2")]);
  });

  it("пробный режим: на сервер ничего не уходит, очередь идёт дальше", async () => {
    const { ctx, visited } = context({ url: RESPONSE("1"), queue: [item("1"), item("2")], result: "dry_run" });
    Object.assign(ctx.state, { queueRunning: true, queueCurrent: item("1"), queueTried: ["1"] });

    await run(new QueueStep(ctx).resume());

    expect(ctx.backend.reportStatus).not.toHaveBeenCalled();
    expect(visited).toEqual([RESPONSE("2")]);
  });

  it("hh.ru не открыл форму: Failed без запуска конвейера", async () => {
    const { ctx } = context({ url: "https://hh.ru/vacancy/999", queue: [item("1")], config: { queueDryRun: false } });
    Object.assign(ctx.state, { queueRunning: true, queueCurrent: item("1"), queueTried: ["1"] });

    await run(new QueueStep(ctx).resume());

    expect(ctx.pipeline.applyOnPage).not.toHaveBeenCalled();
    expect(ctx.backend.reportStatus).toHaveBeenCalledWith("1", expect.objectContaining({ status: "Failed" }), ".NET");
    expect(ctx.state.queueRunning).toBe(false); // других вакансий нет
  });

  it("статус не дошёл до сервера или кнопка неактивна — остановка без перехода", async () => {
    const lost = context({ url: RESPONSE("1"), queue: [item("1"), item("2")], config: { queueDryRun: false } });
    Object.assign(lost.ctx.state, { queueRunning: true, queueCurrent: item("1"), queueTried: ["1"] });
    lost.ctx.backend.reportStatus.mockResolvedValue({ ok: false, error: "сервер ответил 500" } as never);
    await run(new QueueStep(lost.ctx).resume());
    expect(lost.ctx.state.queueRunning).toBe(false);
    expect(lost.visited).toEqual([]);

    const disabled = context({ url: RESPONSE("1"), queue: [item("1"), item("2")], result: "disabled" });
    Object.assign(disabled.ctx.state, { queueRunning: true, queueCurrent: item("1"), queueTried: ["1"] });
    await run(new QueueStep(disabled.ctx).resume());
    expect(disabled.ctx.state.queueRunning).toBe(false);
    expect(disabled.ctx.backend.reportStatus).not.toHaveBeenCalled();
  });

  it("дневной лимит исчерпан — очередь не открывает вакансии", async () => {
    const { ctx, visited } = context({ queue: [item("1")], dailyLimit: 0 });
    await run(new QueueStep(ctx).toggle());
    expect(visited).toEqual([]);
    expect(ctx.state.queueRunning).toBe(false);
  });
});

describe("SearchStep.next", () => {
  const vacancy = (title: string): Vacancy => ({ title, link: "", company: "Acme", card: null, applyBtn: null });

  it("откликается на текущую вакансию и сдвигает указатель", async () => {
    const { ctx } = context({ url: "https://hh.ru/search/vacancy?text=C%23" });
    ctx.state.vacancies = [vacancy("A"), vacancy("B")];

    await run(new SearchStep(ctx).next());

    expect(ctx.pipeline.applyFromSearch).toHaveBeenCalledTimes(1);
    expect(ctx.state.index).toBe(1);
  });

  it("в авто-режиме идёт по всем вакансиям и останавливается в конце", async () => {
    const { ctx } = context({ url: "https://hh.ru/search/vacancy?text=C%23" });
    ctx.state.vacancies = [vacancy("A"), vacancy("B")];
    ctx.state.running = true;

    await run(new SearchStep(ctx).next());

    expect(ctx.pipeline.applyFromSearch).toHaveBeenCalledTimes(2);
    expect(ctx.state.running).toBe(false);
  });

  it("капча сорвала шаг — та же вакансия повторяется", async () => {
    const { ctx } = context({ url: "https://hh.ru/search/vacancy?text=C%23" });
    ctx.state.vacancies = [vacancy("A")];
    ctx.captcha.consumeHit.mockReturnValueOnce(false).mockReturnValueOnce(true);

    await run(new SearchStep(ctx).next());

    expect(ctx.state.index).toBe(0);
  });

  it("лимит исчерпан и неактивная кнопка останавливают авто-режим", async () => {
    const limited = context({ url: "https://hh.ru/search/vacancy?text=C%23", dailyLimit: 0 });
    limited.ctx.state.vacancies = [vacancy("A")];
    limited.ctx.state.running = true;
    await run(new SearchStep(limited.ctx).next());
    expect(limited.ctx.pipeline.applyFromSearch).not.toHaveBeenCalled();
    expect(limited.ctx.state.running).toBe(false);

    const disabled = context({ url: "https://hh.ru/search/vacancy?text=C%23", result: "disabled" });
    disabled.ctx.state.vacancies = [vacancy("A"), vacancy("B")];
    disabled.ctx.state.running = true;
    await run(new SearchStep(disabled.ctx).next());
    expect(disabled.ctx.pipeline.applyFromSearch).toHaveBeenCalledTimes(1);
    expect(disabled.ctx.state.running).toBe(false);
  });
});

describe("VacancyStep", () => {
  it("после отклика возвращает на сохранённый поиск и помечает возобновление", async () => {
    const { ctx, visited } = context({ url: "https://hh.ru/vacancy/1", config: { searchRedirectUrl: "https://hh.ru/search/vacancy?text=QA" } });
    ctx.state.running = true;

    await run(new VacancyStep(ctx).resume());

    expect(ctx.pipeline.applyOnPage).toHaveBeenCalled();
    expect(ctx.state.resumeAfterRedirect).toBe(true);
    expect(visited).toEqual(["https://hh.ru/search/vacancy?text=QA"]);
  });

  it("неактивная кнопка — остановка авто-режима без перехода", async () => {
    const { ctx, visited } = context({ url: "https://hh.ru/vacancy/1", result: "disabled" });
    ctx.state.running = true;

    await run(new VacancyStep(ctx).resume());

    expect(ctx.state.running).toBe(false);
    expect(visited).toEqual([]);
  });
});
