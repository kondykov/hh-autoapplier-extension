import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Vacancy } from "../src/core/state";
import { Logger } from "../src/infra/logger";
import { DEFAULT_RESUMES } from "../src/resume/profiles";
import { ResumeSelector } from "../src/resume/selector";

// Разметка как на живой странице отклика hh.ru (2026-10-24): заголовок резюме внутри [role=button], варианты — [role=option]
function renderForm(current: string, options: string[]) {
  document.body.innerHTML = `
    <div role="button" id="trigger"><span data-qa="resume-title">${current}</span></div>
    <div role="listbox">${options.map((o) => `<div role="option">${o}</div>`).join("")}</div>`;
  for (const option of document.querySelectorAll<HTMLElement>('[role="option"]'))
    option.onclick = () => {
      document.querySelector('[data-qa="resume-title"]')!.textContent = option.textContent;
    };
}

const vacancy = (title: string): Vacancy => ({ title, link: "", company: "", card: null, applyBtn: null });

const PHP = "Fullstack Software Engineer | PHP / Nuxt";
const DOTNET = "Backend Software Engineer | C# .NET developer130 000 ₽";

describe("ResumeSelector", () => {
  const selector = new ResumeSelector(() => DEFAULT_RESUMES, new Logger());

  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  const choose = async (title: string) => {
    const result = selector.choose(vacancy(title));
    await vi.runAllTimersAsync();
    return result;
  };

  it("переключает резюме на подходящее по стеку", async () => {
    renderForm(PHP, [PHP, DOTNET]);
    const result = await choose("Старший разработчик C# .NET");
    expect(result).toEqual({ profile: expect.objectContaining({ name: ".NET" }), failed: false });
    expect(document.querySelector('[data-qa="resume-title"]')!.textContent).toContain("C# .NET");
  });

  it("не трогает список, если нужное резюме уже выбрано", async () => {
    renderForm(DOTNET, [PHP, DOTNET]);
    const click = vi.fn();
    document.getElementById("trigger")!.addEventListener("click", click);
    expect((await choose("C# .NET разработчик")).profile?.name).toBe(".NET");
    expect(click).not.toHaveBeenCalled();
  });

  it("оставляет выбор hh.ru, если стек не указывает на резюме", async () => {
    renderForm(PHP, [PHP, DOTNET]);
    expect(await choose("Менеджер проектов")).toEqual({ profile: null, failed: false });
  });

  it("сообщает о неудаче, если нужного резюме нет в списке", async () => {
    renderForm(PHP, [PHP]);
    expect(await choose("Разработчик C# .NET")).toEqual({ profile: null, failed: true });
  });
});
