import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { BackendClient, type ApiSender } from "../src/api/client";
import { attachLetterAndSubmit } from "../src/apply/submit";
import type { Vacancy } from "../src/core/state";
import { Logger } from "../src/infra/logger";
import { PreparedLetterProvider, StaticTemplateProvider } from "../src/letters/letter-provider";
import { isDryRun, isPageOf, isQueueOn, pickNext, responseUrl, toStatusReport, type QueueItem } from "../src/queue/queue-mode";

const item = (id: string): QueueItem => ({ id, title: `Вакансия ${id}`, company: "Acme", resume: ".NET" });

describe("queue-mode", () => {
  it("строит адрес страницы отклика и узнаёт её после регионального редиректа", () => {
    expect(responseUrl("137435290")).toBe("https://hh.ru/applicant/vacancy_response?vacancyId=137435290");
    expect(isPageOf("https://tver.hh.ru/applicant/vacancy_response?vacancyId=137435290&startedWithQuestion=false", "137435290")).toBe(true);
    expect(isPageOf("https://hh.ru/vacancy/137435290", "137435290")).toBe(true);
    expect(isPageOf("https://hh.ru/applicant/vacancy_response?vacancyId=1", "137435290")).toBe(false);
    expect(isPageOf("https://hh.ru/", "137435290")).toBe(false);
  });

  it("берёт первую ещё не опробованную вакансию", () => {
    const queue = [item("1"), item("2"), item("3")];
    expect(pickNext(queue, [])?.id).toBe("1");
    expect(pickNext(queue, ["1", "2"])?.id).toBe("3");
    expect(pickNext(queue, ["1", "2", "3"])).toBeNull();
  });

  it("переводит исход конвейера в статус сервера", () => {
    expect(toStatusReport("success")).toEqual({ status: "Applied", reason: null });
    expect(toStatusReport("outline")).toEqual({ status: "Skipped", reason: "анкета работодателя — вопросы не найдены на странице" });
    for (const r of ["skipped", "failed", "unclear"] as const) expect(toStatusReport(r)?.status).toBe("Failed");
    // Пробный прогон и неактивная кнопка — серверу не сообщаем
    expect(toStatusReport("archived")).toEqual({ status: "Skipped", reason: "вакансия в архиве" });
    expect(toStatusReport("unavailable")).toEqual({ status: "Skipped", reason: "вакансия недоступна" });
    expect(toStatusReport("dry_run")).toBeNull();
    expect(toStatusReport("disabled")).toBeNull();
    expect(toStatusReport("stopped")).toBeNull();
  });

  // #49: остановленная очередь не отправляет, даже если пробный режим снят
  it("пробный режим — fail-closed", () => {
    expect(isDryRun(true, true)).toBe(true);
    expect(isDryRun(true, false)).toBe(false);
    expect(isDryRun(false, true)).toBe(true);
    expect(isDryRun(false, false)).toBe(true);
  });

  it("очередь ведёт вакансию страницы, пока не остановлена и не сменила вакансию", () => {
    const url = responseUrl("1");
    expect(isQueueOn(true, item("1"), url)).toBe(true);
    expect(isQueueOn(false, item("1"), url)).toBe(false);
    expect(isQueueOn(true, null, url)).toBe(false);
    expect(isQueueOn(true, item("2"), url)).toBe(false);
    expect(isQueueOn(true, item("1"), "")).toBe(false);
  });
});

describe("BackendClient: очередь и статусы", () => {
  it("читает очередь в элементы с резюме от сервера", async () => {
    const send: ApiSender = async (req) => {
      expect(req.path).toBe("/api/queue?limit=20");
      return { status: 200, body: JSON.stringify([{ id: "5", title: "C#", company: "Acme", resumeProfile: ".NET", score: 80 }]) };
    };
    expect(await new BackendClient(send).getQueue()).toEqual({ ok: true, items: [{ id: "5", title: "C#", company: "Acme", resume: ".NET" }] });
  });

  it("берёт из очереди письмо под вакансию, если backend его написал", async () => {
    const body = JSON.stringify([
      { id: "5", title: "C#", company: "Acme", resumeProfile: ".NET", coverLetter: "Здравствуйте, Acme!" },
      { id: "6", title: "C#", company: "Beta", resumeProfile: null, coverLetter: null },
    ]);
    const res = await new BackendClient(async () => ({ status: 200, body })).getQueue();
    expect(res.ok && res.items.map((i) => i.letter)).toEqual(["Здравствуйте, Acme!", undefined]);
  });

  it("отправляет статус и сообщает об ошибках сервера", async () => {
    const calls: unknown[] = [];
    const ok = new BackendClient(async (req) => {
      calls.push(req);
      return { status: 200, body: "{}" };
    });
    expect(await ok.reportStatus("5", { status: "Applied", reason: null }, ".NET")).toEqual({ ok: true });
    expect(calls[0]).toEqual({ type: "hh-api", path: "/api/vacancies/5/status", method: "POST", body: { status: "Applied", reason: null, resume: ".NET" } });

    const conflict = new BackendClient(async () => ({ status: 409, body: '{"message":"Отклик на вакансию уже отправлен"}' }));
    const res = await conflict.reportStatus("5", { status: "Applied", reason: null }, null);
    expect(res.ok).toBe(false);

    const down = new BackendClient(async () => ({ status: 0, body: "", error: "timeout" }));
    expect(await down.getQueue()).toEqual({ ok: false, error: "сервер недоступен: timeout" });
  });
});

describe("attachLetterAndSubmit: пробный режим", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("заполняет письмо, находит кнопку и не нажимает её", async () => {
    document.body.innerHTML = `
      <textarea data-qa="vacancy-response-popup-form-letter-input"></textarea>
      <button data-qa="vacancy-response-submit-popup">Откликнуться</button>`;
    const click = vi.fn();
    document.querySelector("button")!.addEventListener("click", click);

    const result = attachLetterAndSubmit("Здравствуйте", new Logger(), true);
    await vi.runAllTimersAsync();

    expect(await result).toBe("dry_run");
    expect(document.querySelector("textarea")!.value).toBe("Здравствуйте");
    expect(click).not.toHaveBeenCalled();
  });
});

describe("PreparedLetterProvider", () => {
  const fallback = new StaticTemplateProvider(() => "Шаблон для {{company}}");
  const vacancy = (letter?: string): Vacancy => ({ title: "C#", company: "Acme", link: "", letter });

  it("письмо от backend приоритетнее шаблона, пустое — шаблон", async () => {
    const letters = new PreparedLetterProvider(fallback);
    expect(await letters.getLetter(vacancy("  Письмо под вакансию \n"))).toBe("Письмо под вакансию");
    expect(await letters.getLetter(vacancy(" "))).toBe("Шаблон для Acme");
    expect(await letters.getLetter(vacancy())).toBe("Шаблон для Acme");
  });
});
