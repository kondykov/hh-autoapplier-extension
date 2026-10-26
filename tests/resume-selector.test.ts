import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Vacancy } from "../src/core/state";
import { Logger } from "../src/infra/logger";
import { ResumeSelector } from "../src/resume/selector";

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

// Резюме выбирает backend (#27): имя профиля и match — подстрока названия резюме на hh.ru
const vacancy = (resumeMatch?: string): Vacancy => ({ title: "Старший разработчик C# .NET", link: "", company: "", resume: resumeMatch, resumeMatch });

describe("ResumeSelector", () => {
  const selector = new ResumeSelector(new Logger());

  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  const choose = async (match?: string) => {
    const result = selector.choose(vacancy(match));
    await vi.runAllTimersAsync();
    return result;
  };

  const title = () => document.querySelector('[data-qa="resume-title"]')!.textContent;

  it("переключает на резюме, выбранное сервером", async () => {
    renderForm(PHP, [PHP, DOTNET]);
    expect(await choose("C# .NET")).toEqual({ failed: false });
    expect(title()).toContain("C# .NET");
  });

  it("не трогает список, если нужное резюме уже выбрано", async () => {
    renderForm(DOTNET, [PHP, DOTNET]);
    const click = vi.fn();
    document.getElementById("trigger")!.addEventListener("click", click);
    expect(await choose("C# .NET")).toEqual({ failed: false });
    expect(click).not.toHaveBeenCalled();
  });

  it("сервер не выбрал резюме — остаётся выбор hh.ru, стек вакансии не важен", async () => {
    renderForm(PHP, [PHP, DOTNET]);
    expect(await choose(undefined)).toEqual({ failed: false });
    expect(title()).toBe(PHP);
  });

  it("сообщает о неудаче, если нужного резюме нет в списке", async () => {
    renderForm(PHP, [PHP]);
    expect(await choose("C# .NET")).toEqual({ failed: true });
  });
});
