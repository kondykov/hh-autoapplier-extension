import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Vacancy } from "../src/core/state";
import { Logger } from "../src/infra/logger";
import { isSameResume, ResumeSelector } from "../src/resume/selector";

// Разметка как на живой странице отклика hh.ru (2026-10-24): заголовок резюме внутри [role=button], варианты — [role=option]
function renderForm(current: string, options: string[]) {
  document.body.innerHTML = `
    <div role="button" id="trigger"><span data-qa="resume-title">${current}</span></div>
    <div role="listbox">${options.map((o) => `<div role="option">${o}</div>`).join("")}</div>`;
  for (const option of Array.from(document.querySelectorAll<HTMLElement>('[role="option"]')))
    option.onclick = () => {
      document.querySelector('[data-qa="resume-title"]')!.textContent = option.textContent;
    };
}

const PHP = "Fullstack Software Engineer | PHP / Nuxt";
const DOTNET_NAME = "Backend Software Engineer | C# .NET developer";
const DOTNET = `${DOTNET_NAME}130 000 ₽`;

// Резюме выбирает backend (#27): имя профиля и match — полное название резюме на hh.ru
const vacancy = (resumeMatch?: string): Vacancy => ({ title: "Старший разработчик C# .NET", link: "", company: "", resume: resumeMatch, resumeMatch });

describe("isSameResume", () => {
  it("название целиком, с приклеенной зарплатой или без", () => {
    expect(isSameResume(DOTNET_NAME, DOTNET_NAME)).toBe(true);
    expect(isSameResume(DOTNET, DOTNET_NAME)).toBe(true);
    expect(isSameResume("Backend .NET  150 000 ₽", "Backend .NET")).toBe(true);
  });

  it("часть названия не подходит", () => {
    expect(isSameResume(DOTNET, "C# .NET")).toBe(false);
    expect(isSameResume("Senior Backend", "Backend")).toBe(false);
    expect(isSameResume(null, "Backend")).toBe(false);
  });

  it("цифры в конце названия без валюты — не зарплата", () => {
    expect(isSameResume("Backend 2", "Backend")).toBe(false);
    expect(isSameResume("Backend 2", "Backend 2")).toBe(true);
  });
});

describe("ResumeSelector", () => {
  let messages: { text: string; level: string }[];
  let selector: ResumeSelector;

  beforeEach(() => {
    vi.useFakeTimers();
    messages = [];
    const logger = new Logger();
    logger.addSink({ write: (text, level) => messages.push({ text, level }) });
    selector = new ResumeSelector(logger);
  });
  afterEach(() => vi.useRealTimers());

  const choose = async (match?: string) => {
    const result = selector.choose(vacancy(match));
    await vi.runAllTimersAsync();
    return result;
  };

  const title = () => document.querySelector('[data-qa="resume-title"]')!.textContent;

  it("переключает на резюме, выбранное сервером", async () => {
    renderForm(PHP, [PHP, DOTNET]);
    expect(await choose(DOTNET_NAME)).toEqual({ failed: false });
    expect(title()).toBe(DOTNET);
  });

  it("не трогает список, если нужное резюме уже выбрано", async () => {
    renderForm(DOTNET, [PHP, DOTNET]);
    const click = vi.fn();
    document.getElementById("trigger")!.addEventListener("click", click);
    expect(await choose(DOTNET_NAME)).toEqual({ failed: false });
    expect(click).not.toHaveBeenCalled();
  });

  it("сервер не выбрал резюме — остаётся выбор hh.ru, стек вакансии не важен", async () => {
    renderForm(PHP, [PHP, DOTNET]);
    expect(await choose(undefined)).toEqual({ failed: false });
    expect(title()).toBe(PHP);
  });

  it("сообщает о неудаче, если нужного резюме нет в списке", async () => {
    renderForm(PHP, [PHP]);
    expect(await choose(DOTNET_NAME)).toEqual({ failed: true });
  });

  it("подстрока названия — ошибка в журнал, резюме не меняется", async () => {
    renderForm(PHP, [PHP, "Backend .NET", "Senior Backend"]);
    expect(await choose("Backend")).toEqual({ failed: true });
    expect(title()).toBe(PHP);
    expect(messages.some((m) => m.level === "error" && m.text.includes("нет точного совпадения"))).toBe(true);
  });

  it("уже выбранное резюме с подстрокой match не считается нужным", async () => {
    renderForm("Senior Backend", ["Senior Backend", "Backend"]);
    expect(await choose("Backend")).toEqual({ failed: false });
    expect(title()).toBe("Backend");
  });

  it("несколько одинаковых вариантов — ошибка, наугад не выбирает", async () => {
    renderForm(PHP, [PHP, "Backend 100 ₽", "Backend 200 ₽"]);
    expect(await choose("Backend")).toEqual({ failed: true });
    expect(title()).toBe(PHP);
    expect(messages.some((m) => m.level === "error" && m.text.includes("подходит несколько (2)"))).toBe(true);
  });
});
