import { SELECTORS } from "../core/selectors";
import { firstMatch } from "../dom/query";
import { extractSalary, extractStack, extractWorkFormat } from "../report/extractors";
import { detectPageType } from "../dom/page-type";

export interface VacancyInfo {
  title: string;
  company: string;
  salary: string;
  format: string;
  stack: string[];
}

const UNKNOWN_TITLE = "Неизвестная вакансия";
const UNKNOWN_COMPANY = "Неизвестная компания";

export function extractVacancyInfo(): VacancyInfo {
  let title = UNKNOWN_TITLE;
  let company = UNKNOWN_COMPANY;

  if (detectPageType() === "response") {
    const matched = firstMatch([SELECTORS.responseCredsTitle, SELECTORS.responseMainTitle]);
    const candidate = matched?.el.textContent?.trim();
    // «Отклик на вакансию» в h1 — служебный заголовок, а не название
    if (candidate && candidate !== "Отклик на вакансию") title = candidate;
    const avatar = document.querySelector(SELECTORS.responseCompanyAvatar);
    company = avatar?.getAttribute("aria-label")?.trim() || company;
  }

  if (title === UNKNOWN_TITLE && document.title) {
    title = document.title.replace(/\s*[—-]\s*hh\.ru$/i, "").trim() || title;
  }
  const text = (sel: string) => document.querySelector(sel)?.textContent ?? "";
  const skills = Array.from(document.querySelectorAll(SELECTORS.pageSkills)).map((e) => e.textContent ?? "");
  const stack = extractStack(title, skills.join(" "), text(SELECTORS.pageDescription));
  return {
    title,
    company,
    salary: extractSalary(text(SELECTORS.pageSalary)),
    format: extractWorkFormat(text(SELECTORS.pageWorkFormat)),
    stack: skills.length ? [...new Set([...skills.map((s) => s.trim()), ...stack])] : stack,
  };
}
