import { SELECTORS } from "../core/selectors";
import type { AppState, Vacancy } from "../core/state";
import { extractSalary, extractStack, extractWorkFormat } from "../report/extractors";
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
    const labels = qsa(SELECTORS.cardFormatLabels, card).map((e) => e.textContent).join(" ");
    const snippets = qsa(SELECTORS.snippets, card).map((e) => e.textContent).join(" ");
    const title = normalizeText(titleEl?.textContent) || `Вакансия #${idx + 1}`;
    found.push({
      salary: extractSalary(card.textContent),
      format: extractWorkFormat(`${labels} ${card.textContent}`),
      stack: extractStack(title, snippets),
      title: normalizeText(titleEl?.textContent) || `Вакансия #${idx + 1}`,
      company: normalizeText(qs(SELECTORS.companyName, card)?.textContent) || "Неизвестная компания",
      link,
      applyBtn,
      card,
    });
  });
  return found;
}
