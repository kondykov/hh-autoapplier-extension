import { SELECTORS } from "../core/selectors";
import type { AppState, Vacancy } from "../core/state";
import { isAlreadyResponded, normalizeText, qs, qsa } from "../dom/query";
import type { Logger } from "../infra/logger";

export function collectVacancies(state: AppState, logger: Logger): Vacancy[] {
  const found: Vacancy[] = [];
  qsa(SELECTORS.vacancyCard).forEach((card, idx) => {
    const applyBtn = qs<HTMLElement>(SELECTORS.applyButton, card);
    if (!applyBtn || isAlreadyResponded(card)) return;

    const titleEl = qs<HTMLAnchorElement>(SELECTORS.vacancyTitleLink, card) ?? qs<HTMLAnchorElement>(SELECTORS.vacancyTitle, card);
    const link = titleEl?.href ?? "";
    if (state.isSkipped(link)) {
      logger.log(`Пропускаю вакансию из аутлайна: ${link}`);
      return;
    }
    found.push({
      title: normalizeText(titleEl?.textContent) || `Вакансия #${idx + 1}`,
      company: normalizeText(qs(SELECTORS.companyName, card)?.textContent) || "Неизвестная компания",
      link,
      applyBtn,
      card,
    });
  });
  return found;
}
