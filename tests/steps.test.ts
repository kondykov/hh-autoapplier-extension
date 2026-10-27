import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { AutoReply } from "../src/api/client";
import type { ApplyResult } from "../src/apply/pipeline";
import { DEFAULT_CONFIG, type AppConfig } from "../src/core/config";
import { DailyLimit } from "../src/core/daily-limit";
import { AppState, type Vacancy } from "../src/core/state";
import type { DialogChat } from "../src/dialogs/chats";
import { Logger } from "../src/infra/logger";
import { MemoryStorage } from "../src/infra/storage";
import { DialogStep } from "../src/pages/dialog-step";
import { QueueStep } from "../src/pages/queue-step";
import { SearchStep } from "../src/pages/search-step";
import { pickStep, type StepContext } from "../src/pages/step-context";
import type { QueueItem } from "../src/queue/queue-mode";
import type { ImportCard } from "../src/search/vacancy-collector";
import { renderResponsePage } from "./fixtures/response-form";

const item = (id: string): QueueItem => ({ id, title: `Вакансия ${id}`, company: "Acme", resume: ".NET" });
const RESPONSE = (id: string) => `https://hh.ru/applicant/vacancy_response?vacancyId=${id}`;

// Контекст шага без браузера: навигация, панель, backend и конвейер — подменные
function context(opts: { url?: string; queue?: QueueItem[]; result?: ApplyResult; config?: Partial<AppConfig>; dailyLimit?: number; autoReplies?: AutoReply[] } = {}) {
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
      setQueueActive: vi.fn(),
      setRejectActive: vi.fn(),
    },
    backend: {
      getQueue: vi.fn(async () => ({ ok: true as const, items: opts.queue ?? [] })),
      getResumes: vi.fn(async () => ({ ok: true as const, resumes: [{ name: ".NET", match: "C# .NET" }] })),
      importVacancies: vi.fn(async (_search: string, _cards: ImportCard[]) => ({ ok: true as const, result: { received: 2, added: 2, queued: 1, skipped: 1, waiting: 0 } })),
      reportStatus: vi.fn(async () => ({ ok: true as const })),
      submitQuestionnaire: vi.fn(async () => ({ ok: true as const })),
      reportEvent: vi.fn(async () => ({ ok: true as const })),
      importDialogs: vi.fn(async (chats: DialogChat[]) => ({
        ok: true as const,
        result: { received: chats.length, newMessages: 1, rejected: 1, invited: 0, autoReplies: opts.autoReplies ?? [] },
      })),
      reportAutoReply: vi.fn(async (_chatId: number, _sent: boolean, _error?: string) => ({ ok: true as const })),
    },
    pipeline: {
      applyOnPage: vi.fn(async (_v: Vacancy) => opts.result ?? "success"),
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
  it("после загрузки страницы работу продолжает только очередь", () => {
    const { ctx } = context();
    const steps = [new QueueStep(ctx)];
    const state = new AppState();

    expect(pickStep(steps, "search", state)).toBeNull();
    state.queueRunning = true;
    expect(pickStep(steps, "search", state)?.name).toBe("очередь");
    expect(pickStep(steps, "unknown", state)?.name).toBe("очередь");
  });
});

describe("QueueStep", () => {
  it("запуск: открывает страницу отклика первой вакансии с match резюме сервера", async () => {
    const { ctx, visited } = context({ queue: [item("1"), item("2")] });

    await run(new QueueStep(ctx).toggle());

    expect(ctx.state.queueRunning).toBe(true);
    expect(ctx.state.queueCurrent).toMatchObject({ id: "1", resume: ".NET", resumeMatch: "C# .NET" });
    expect(visited).toEqual([RESPONSE("1")]);
  });

  it("резюме с сервера недоступны — очередь останавливается, вакансия не открывается", async () => {
    const { ctx, visited, messages } = context({ queue: [item("1")] });
    ctx.backend.getResumes.mockResolvedValue({ ok: false, error: "сервер недоступен: timeout" } as never);

    await run(new QueueStep(ctx).toggle());

    expect(ctx.state.queueRunning).toBe(false);
    expect(visited).toEqual([]);
    expect(messages.at(-1)).toContain("Резюме с сервера недоступны");
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
    Object.assign(ctx.state, { queueRunning: true, queueCurrent: { ...item("1"), resumeMatch: "C# .NET", letter: "Письмо под вакансию" }, queueTried: ["1"] });

    await run(new QueueStep(ctx).resume());

    const v = ctx.pipeline.applyOnPage.mock.calls[0][0];
    expect(v.resume).toBe(".NET");
    expect(v.resumeMatch).toBe("C# .NET");
    expect(v.letter).toBe("Письмо под вакансию");
    expect(ctx.backend.reportStatus).toHaveBeenCalledWith("1", { status: "Applied", reason: null }, ".NET");
    expect(visited).toEqual([RESPONSE("2")]);
  });

  it("отклик ушёл, а очередь остановили или включили пробный режим — статус всё равно на сервер, дальше не идёт", async () => {
    const { ctx, visited } = context({ url: RESPONSE("1"), queue: [item("1"), item("2")], result: "success", config: { queueDryRun: false } });
    Object.assign(ctx.state, { queueRunning: true, queueCurrent: item("1"), queueTried: ["1"] });
    // Пока конвейер ждал подтверждения отклика, владелец остановил очередь и поставил галочку пробного режима
    ctx.pipeline.applyOnPage.mockImplementation(async () => {
      Object.assign(ctx.state, { queueRunning: false });
      ctx.config.queueDryRun = true;
      return "success";
    });

    await run(new QueueStep(ctx).resume());

    expect(ctx.backend.reportStatus).toHaveBeenCalledWith("1", { status: "Applied", reason: null }, ".NET");
    expect(visited).toEqual([]);
  });

  it("пробный режим: на сервер ничего не уходит, очередь идёт дальше", async () => {
    const { ctx, visited } = context({ url: RESPONSE("1"), queue: [item("1"), item("2")], result: "dry_run" });
    Object.assign(ctx.state, { queueRunning: true, queueCurrent: item("1"), queueTried: ["1"] });

    await run(new QueueStep(ctx).resume());

    expect(ctx.backend.reportStatus).not.toHaveBeenCalled();
    expect(visited).toEqual([RESPONSE("2")]);
  });

  it("анкета без ответов: вопросы на сервер, статус не отправляется, очередь идёт дальше", async () => {
    renderResponsePage({ questionnaire: true });
    const { ctx, visited } = context({ url: RESPONSE("1"), queue: [item("1"), item("2")], result: "outline", config: { queueDryRun: false } });
    Object.assign(ctx.state, { queueRunning: true, queueCurrent: item("1"), queueTried: ["1"] });

    await run(new QueueStep(ctx).resume());

    expect(ctx.backend.submitQuestionnaire).toHaveBeenCalledWith("1", expect.arrayContaining([expect.objectContaining({ type: "Radio" })]));
    expect(ctx.backend.reportStatus).not.toHaveBeenCalled();
    expect(visited).toEqual([RESPONSE("2")]);
  });

  it("анкета не распозналась — Skipped, как раньше", async () => {
    document.body.innerHTML = "";
    const { ctx } = context({ url: RESPONSE("1"), queue: [item("1")], result: "outline", config: { queueDryRun: false } });
    Object.assign(ctx.state, { queueRunning: true, queueCurrent: item("1"), queueTried: ["1"] });

    await run(new QueueStep(ctx).resume());

    expect(ctx.backend.submitQuestionnaire).not.toHaveBeenCalled();
    expect(ctx.backend.reportStatus).toHaveBeenCalledWith("1", expect.objectContaining({ status: "Skipped" }), ".NET");
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
    expect(ctx.backend.reportEvent).toHaveBeenCalledWith("DailyLimit", expect.stringContaining("0"));
  });
});

describe("SearchStep.send — выдача на сервер (#27)", () => {
  const SEARCH = "https://hh.ru/search/vacancy?text=C%23";
  const card = (id: string, title: string, responded = false) => `
    <div data-qa="vacancy-serp__vacancy">
      <a data-qa="serp-item__title" href="https://tver.hh.ru/vacancy/${id}?query=C%23">${title}</a>
      <div data-qa="vacancy-serp__vacancy-employer">Acme</div>
      <div data-qa="vacancy-serp__vacancy_snippet_requirement">Опыт с ASP.NET Core и PostgreSQL</div>
      ${responded ? "<span data-qa=\"vacancy-serp__vacancy_responded\"></span>" : ""}
    </div>`;

  beforeEach(() => {
    document.body.innerHTML = card("1", "C# разработчик") + card("2", "Backend .NET") + card("3", "Уже откликались", true);
  });

  it("карточки без отклика уходят в поиск на сервере, итог — в журнал", async () => {
    const { ctx, messages } = context({ url: SEARCH, config: { importSearch: "dotnet" } });

    expect(await new SearchStep(ctx).send()).toBe(true);

    const [search, cards] = ctx.backend.importVacancies.mock.calls[0];
    expect(search).toBe("dotnet");
    expect(cards.map((c) => c.id)).toEqual(["1", "2"]);
    expect(cards[0]).toMatchObject({ title: "C# разработчик", company: "Acme", url: "https://hh.ru/vacancy/1" });
    expect(cards[0].description).toContain("ASP.NET Core");
    expect(messages.at(-1)).toContain("получено 2, новых 2");
  });

  it("без поиска на сервере и не на выдаче — ничего не отправляет", async () => {
    const noSearch = context({ url: SEARCH });
    expect(await new SearchStep(noSearch.ctx).send()).toBe(false);
    expect(noSearch.messages.at(-1)).toContain("поиск");

    const notSearch = context({ url: "https://hh.ru/vacancy/1", config: { importSearch: "dotnet" } });
    expect(await new SearchStep(notSearch.ctx).send()).toBe(false);
    expect(notSearch.ctx.backend.importVacancies).not.toHaveBeenCalled();
    expect(noSearch.ctx.backend.importVacancies).not.toHaveBeenCalled();
  });

  it("ошибка сервера (нет такого поиска) — в журнал", async () => {
    const { ctx, messages } = context({ url: SEARCH, config: { importSearch: "нет такого" } });
    ctx.backend.importVacancies.mockResolvedValue({ ok: false, error: "сервер ответил 400: поиск «нет такого» не найден" } as never);

    expect(await new SearchStep(ctx).send()).toBe(false);
    expect(messages.at(-1)).toContain("не найден");
  });
});

describe("DialogStep", () => {
  const CHATS = "https://tver.hh.ru/chat";
  const reply = (chatId: number): AutoReply => ({ chatId, vacancyId: String(chatId), title: `C# ${chatId}`, company: "Acme" });
  const chat = (chatId: number): DialogChat => ({
    chatId, vacancyId: String(chatId), title: "C#", company: "Acme", state: "DISCARD", messageId: chatId * 10, text: "К сожалению…", at: "2026-10-26T10:00:00+03:00", fromEmployer: true,
  });
  const reader = (writable: number[]) => ({
    recent: vi.fn(async () => [chat(1), chat(2)]),
    writePossibility: vi.fn(async (id: number) => (writable.includes(id) ? { canWrite: true, reason: null } : { canWrite: false, reason: "WITHOUT_INVITATION" })),
  });

  it("чаты — на сервер; закрытая переписка — автоответ невозможен; пробный режим ничего не отправляет", async () => {
    const { ctx, visited, messages } = context({ url: CHATS, autoReplies: [reply(1), reply(2)] });
    const chats = reader([2]);

    await run(new DialogStep(ctx, chats, vi.fn()).run());

    expect(ctx.backend.importDialogs.mock.calls[0][0].map((c) => c.chatId)).toEqual([1, 2]);
    expect(ctx.backend.reportAutoReply).toHaveBeenCalledTimes(1);
    expect(ctx.backend.reportAutoReply).toHaveBeenCalledWith(1, false, expect.stringContaining("после приглашения"));
    expect(messages.some((m) => m.includes("Пробный режим") && m.includes("C# 2"))).toBe(true);
    expect(ctx.state.autoReplies).toEqual([]);
    expect(visited).toEqual([]);
  });

  it("не на странице чатов — ничего не собирает", async () => {
    const { ctx } = context({ url: "https://hh.ru/vacancy/1" });
    const chats = reader([]);

    await run(new DialogStep(ctx, chats, vi.fn()).run());

    expect(chats.recent).not.toHaveBeenCalled();
    expect(ctx.backend.importDialogs).not.toHaveBeenCalled();
  });

  it("без пробного режима: открывает чат, отправляет шаблон, переходит к следующему", async () => {
    const { ctx, visited, setUrl } = context({ url: CHATS, autoReplies: [reply(1), reply(2)], config: { queueDryRun: false, rejectionReply: "Спасибо!" } });
    const send = vi.fn(async () => ({ ok: true as const }));
    const step = new DialogStep(ctx, reader([1, 2]), send);

    await run(step.run());
    expect(visited).toEqual(["https://tver.hh.ru/chat/1"]);
    expect(step.accepts("chat", ctx.state)).toBe(true);

    await run(step.resume());
    expect(send).toHaveBeenCalledWith("Спасибо!");
    expect(ctx.backend.reportAutoReply).toHaveBeenCalledWith(1, true, undefined);
    expect(visited.at(-1)).toBe("https://tver.hh.ru/chat/2");

    // hh.ru открыл не тот чат — в чужой не пишем
    setUrl("https://tver.hh.ru/chat/999");
    await run(step.resume());
    expect(send).toHaveBeenCalledTimes(1);
    expect(ctx.backend.reportAutoReply).toHaveBeenLastCalledWith(2, false, "чат не открылся");
    expect(ctx.state.autoReplies).toEqual([]);
    expect(step.accepts("chat", ctx.state)).toBe(false);
  });

  it("пробный режим включили во время автоответов — остановиться, не отправляя", async () => {
    const { ctx } = context({ url: "https://tver.hh.ru/chat/1" });
    ctx.state.autoReplies = [reply(1)];
    const send = vi.fn();

    await run(new DialogStep(ctx, reader([1]), send).resume());

    expect(send).not.toHaveBeenCalled();
    expect(ctx.backend.reportAutoReply).not.toHaveBeenCalled();
    expect(ctx.state.autoReplies).toEqual([]);
  });
});
