import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Vacancy } from "../src/core/state";
import { Logger } from "../src/infra/logger";
import { resumeName, resumeRule, ResumeSelector } from "../src/resume/selector";

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
const DOTNET = "Backend Software Engineer | C# .NET developer130 000 ₽";

// Резюме выбирает backend (#27): имя профиля и match — часть названия резюме на hh.ru
const vacancy = (resumeMatch?: string): Vacancy => ({ title: "Старший разработчик C# .NET", link: "", company: "", resume: resumeMatch, resumeMatch });

describe("resumeName", () => {
  it("без приклеенной зарплаты; цифры без валюты — часть названия", () => {
    expect(resumeName(DOTNET)).toBe("Backend Software Engineer | C# .NET developer");
    expect(resumeName("Backend .NET  150 000 ₽")).toBe("Backend .NET");
    expect(resumeName("Backend 2")).toBe("Backend 2");
    expect(resumeName(null)).toBe("");
  });
});

describe("resumeRule", () => {
  const pick = (names: string[], match: string) => {
    const fits = resumeRule(names, match);
    return fits ? names.filter(fits) : null;
  };

  it("единственная подстрока", () => expect(pick(["Backend .NET", "PHP"], ".NET")).toEqual(["Backend .NET"]));
  it("точное совпадение важнее подстрок", () => expect(pick(["Senior Backend", "Backend", "Backend .NET"], "Backend")).toEqual(["Backend"]));
  it("несколько подстрок — null", () => expect(pick(["Backend .NET", "Senior Backend"], "Backend")).toBeNull());
  it("несколько точных — null", () => expect(pick(["Backend", "Backend"], "Backend")).toBeNull());
  it("ни одного — null", () => expect(pick(["PHP"], "Backend")).toBeNull());
  it("зарплата в подстроку не попадает", () => expect(pick([resumeName("PHP 130 000 ₽")], "130")).toBeNull());
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
  const hasError = (text: string) => messages.some((m) => m.level === "error" && m.text.includes(text));

  it("переключает на резюме по части названия", async () => {
    renderForm(PHP, [PHP, DOTNET]);
    expect(await choose("C# .NET")).toEqual({ failed: false });
    expect(title()).toBe(DOTNET);
  });

  it("текущее совпало точно (без зарплаты) — список не открывает", async () => {
    renderForm(DOTNET, [PHP, DOTNET]);
    const click = vi.fn();
    document.getElementById("trigger")!.addEventListener("click", click);
    expect(await choose("Backend Software Engineer | C# .NET developer")).toEqual({ failed: false });
    expect(click).not.toHaveBeenCalled();
  });

  it("текущее содержит match и в списке оно одно — остаётся", async () => {
    renderForm(DOTNET, [PHP, DOTNET]);
    const click = vi.fn();
    document.getElementById("trigger")!.addEventListener("click", click);
    expect(await choose("C# .NET")).toEqual({ failed: false });
    expect(click).toHaveBeenCalledTimes(1);
    expect(title()).toBe(DOTNET);
  });

  it("текущее содержит match, но в списке подходящих несколько — ошибка, а не молчаливый выбор текущего", async () => {
    renderForm("Senior Backend", ["Backend .NET", "Senior Backend"]);
    expect(await choose("Backend")).toEqual({ failed: true });
    expect(title()).toBe("Senior Backend");
    expect(hasError("подходит несколько (2)")).toBe(true);
  });

  it("сервер не выбрал резюме — остаётся выбор hh.ru, стек вакансии не важен", async () => {
    renderForm(PHP, [PHP, DOTNET]);
    expect(await choose(undefined)).toEqual({ failed: false });
    expect(title()).toBe(PHP);
  });

  it("нужного резюме нет в списке — ошибка со списком вариантов", async () => {
    renderForm(PHP, [PHP]);
    expect(await choose("C# .NET")).toEqual({ failed: true });
    expect(hasError("нет совпадения")).toBe(true);
    expect(hasError(PHP)).toBe(true);
  });

  it("неоднозначная подстрока — ошибка, резюме не меняется", async () => {
    renderForm(PHP, [PHP, "Backend .NET", "Senior Backend"]);
    expect(await choose("Backend")).toEqual({ failed: true });
    expect(title()).toBe(PHP);
    expect(hasError("подходит несколько (2)")).toBe(true);
  });

  it("точное совпадение важнее подстроки, в том числе при текущем с подстрокой", async () => {
    renderForm("Senior Backend", ["Senior Backend", "Backend 100 ₽", "Backend .NET"]);
    expect(await choose("Backend")).toEqual({ failed: false });
    expect(title()).toBe("Backend 100 ₽");
  });

  it("клик по точному варианту не сработал — неудача, хотя текущее содержит match", async () => {
    renderForm("Senior Backend", ["Senior Backend", "Backend"]);
    for (const option of Array.from(document.querySelectorAll<HTMLElement>('[role="option"]'))) option.onclick = null;
    expect(await choose("Backend")).toEqual({ failed: true });
    expect(hasError("Не удалось выбрать")).toBe(true);
  });
});
