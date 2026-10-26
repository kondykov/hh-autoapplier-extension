import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { BackendClient } from "../src/api/client";
import { ApplyPipeline, type ApplyResult } from "../src/apply/pipeline";
import { AppState, type Vacancy } from "../src/core/state";
import { getCoverLetterField } from "../src/dom/cover-letter";
import { Logger } from "../src/infra/logger";
import { StaticTemplateProvider } from "../src/letters/letter-provider";
import { fillQuestionnaire, toServerQuestions, type QuestionAnswer } from "../src/questions/fill";
import { ResumeSelector } from "../src/resume/selector";
import { letterValue, QUESTIONS, renderResponsePage, resetSubmits, submits } from "./fixtures/response-form";

const setUrl = (url: string) => (window as unknown as { happyDOM: { setURL(url: string): void } }).happyDOM.setURL(url);

const ANSWERS: QuestionAnswer[] = [
  { question: QUESTIONS.solid, answer: ["Dependency Inversion Principle"] },
  { question: QUESTIONS.agree, answer: ["Да"] },
  { question: QUESTIONS.salary, answer: ["от 250 000 ₽"] },
];

const checked = (value: string) => document.querySelector<HTMLInputElement>(`input[value="${value}"]`)!.checked;
const salary = () => document.querySelector<HTMLTextAreaElement>('textarea[name="task_3_text"]')!.value;

beforeEach(() => {
  setUrl("https://hh.ru/applicant/vacancy_response?vacancyId=1");
  resetSubmits();
});

describe("fillQuestionnaire", () => {
  it("отмечает варианты по тексту и пишет ответ в текстовое поле", () => {
    renderResponsePage({ questionnaire: true });
    expect(fillQuestionnaire(ANSWERS)).toEqual({ ok: true, missing: [] });
    expect([checked("11"), checked("12"), checked("21"), checked("22")]).toEqual([false, true, true, false]);
    expect(salary()).toBe("от 250 000 ₽");
  });

  it("сверяет вопрос и вариант без учёта регистра и пробелов; нет ответа или варианта — не ok", () => {
    renderResponsePage({ questionnaire: true });
    const result = fillQuestionnaire([
      { question: `  ${QUESTIONS.solid.toUpperCase()} `, answer: ["dependency  inversion principle"] },
      { question: QUESTIONS.agree, answer: ["Возможно"] },
    ]);
    expect(checked("12")).toBe(true);
    expect(result).toEqual({ ok: false, missing: [QUESTIONS.agree, QUESTIONS.salary] });
  });

  it("поле ответа анкеты не принимается за поле письма", () => {
    renderResponsePage({ questionnaire: true });
    expect(getCoverLetterField()).toBeNull();
  });

  it("вопросы для сервера — с типами backend", () => {
    renderResponsePage({ questionnaire: true });
    expect(toServerQuestions()).toEqual([
      { question: QUESTIONS.solid, type: "Radio", options: ["Single Responsibility Principle", "Dependency Inversion Principle"] },
      { question: QUESTIONS.agree, type: "Checkbox", options: ["Да", "Нет"] },
      { question: QUESTIONS.salary, type: "Text", options: [] },
    ]);
  });
});

describe("ApplyPipeline: анкета с ответами backend", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  function pipeline(dryRun: boolean) {
    const logger = new Logger();
    const onOutline = vi.fn(async () => {});
    const p = new ApplyPipeline({
      state: new AppState(),
      logger,
      letters: new StaticTemplateProvider(() => "Письмо"),
      resumes: new ResumeSelector(logger),
      onApplied: vi.fn(async () => {}),
      onOutline,
      onChange: vi.fn(async () => {}),
      dryRun: () => dryRun,
    });
    return { p, onOutline };
  }

  const vacancy = (answers?: QuestionAnswer[]): Vacancy => ({
    title: "Разработчик C# .NET", company: "Acme", link: "", resume: ".NET", resumeMatch: "C# .NET", answers,
  });

  async function run(promise: Promise<ApplyResult>): Promise<ApplyResult> {
    await vi.runAllTimersAsync();
    return promise;
  }

  it("без ответов — анкета, как раньше", async () => {
    renderResponsePage({ questionnaire: true });
    const { p, onOutline } = pipeline(false);
    expect(await run(p.applyOnPage(vacancy()))).toBe("outline");
    expect(onOutline).toHaveBeenCalled();
    expect(submits).toBe(0);
  });

  it("с ответами: заполняет анкету, письмо — в поле письма, пробный режим не отправляет", async () => {
    renderResponsePage({ questionnaire: true });
    const { p } = pipeline(true);
    expect(await run(p.applyOnPage(vacancy(ANSWERS)))).toBe("dry_run");
    expect(checked("12")).toBe(true);
    expect(salary()).toBe("от 250 000 ₽");
    expect(letterValue()).toBe("Письмо");
    expect(submits).toBe(0);
  });

  it("с ответами: отправляет", async () => {
    renderResponsePage({ questionnaire: true });
    const { p } = pipeline(false);
    expect(await run(p.applyOnPage(vacancy(ANSWERS)))).toBe("success");
    expect(submits).toBe(1);
  });

  it("анкета на странице не совпала с ответами — не отправляет", async () => {
    renderResponsePage({ questionnaire: true });
    const { p, onOutline } = pipeline(false);
    expect(await run(p.applyOnPage(vacancy(ANSWERS.slice(0, 2))))).toBe("outline");
    expect(onOutline).toHaveBeenCalled();
    expect(submits).toBe(0);
  });
});

describe("BackendClient: анкеты", () => {
  it("ответы приходят с очередью, только если отвечено на всё", async () => {
    const body = JSON.stringify([
      { id: "1", title: "C#", company: "Acme", resumeProfile: ".NET", questions: [{ question: "Q", type: "Radio", options: ["A"], answer: ["A"] }] },
      { id: "2", title: "C#", company: "Acme", resumeProfile: ".NET", questions: [{ question: "Q", type: "Text", options: [], answer: [] }] },
      { id: "3", title: "C#", company: "Acme", resumeProfile: ".NET", questions: [] },
    ]);
    const res = await new BackendClient(async () => ({ status: 200, body })).getQueue();
    expect(res.ok && res.items.map((i) => i.answers)).toEqual([[{ question: "Q", answer: ["A"] }], undefined, undefined]);
  });

  it("отправляет вопросы анкеты", async () => {
    const calls: unknown[] = [];
    const client = new BackendClient(async (req) => {
      calls.push(req);
      return { status: 200, body: "{}" };
    });
    const questions = [{ question: "Q", type: "Text", options: [] }];
    expect(await client.submitQuestionnaire("5", questions)).toEqual({ ok: true });
    expect(calls[0]).toEqual({ type: "hh-api", path: "/api/vacancies/5/questionnaire", method: "POST", body: { questions } });
  });
});

describe("BackendClient: события для Telegram", () => {
  it("отправляет событие; недоступный сервер — ошибка, а не исключение", async () => {
    const calls: unknown[] = [];
    const client = new BackendClient(async (req) => {
      calls.push(req);
      return { status: 200, body: '{"notified":true}' };
    });
    expect(await client.reportEvent("Captcha", "https://hh.ru/vacancy/1")).toEqual({ ok: true });
    expect(calls[0]).toEqual({ type: "hh-api", path: "/api/events", method: "POST", body: { kind: "Captcha", message: "https://hh.ru/vacancy/1" } });

    const offline = new BackendClient(async () => { throw new Error("нет фона"); });
    expect(await offline.reportEvent("DailyLimit")).toEqual({ ok: false, error: "сервер недоступен: нет фона" });
  });
});
