import { SELECTORS } from "../core/selectors";
import { firstMatch } from "../dom/query";
import { detectPageType } from "../dom/page-type";

export interface VacancyInfo {
  title: string;
  company: string;
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
  return { title, company };
}
