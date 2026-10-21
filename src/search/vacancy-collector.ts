import { SELECTORS } from "../core/selectors";
import type { AppState, Vacancy } from "../core/state";
import { extractSalary, extractStack, extractWorkFormat } from "../report/extractors";
import { EMPTY_RULES, evaluate, type FilterRules } from "../filter/rules";
import { isAlreadyResponded, normalizeText, qs, qsa } from "../dom/query";
import type { Logger } from "../infra/logger";

export function collectVacancies(state: AppState, logger: Logger, rules: FilterRules = EMPTY_RULES): Vacancy[] {
  const found: Vacancy[] = [];
  let filtered = 0;
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
    const salary = extractSalary(card.textContent);
    const format = extractWorkFormat(`${labels} ${card.textContent}`);
    const stack = extractStack(title, snippets);
    const verdict = evaluate({ title, text: snippets, stack, salary, format }, rules);
    if (!verdict.pass) {
      logger.log(`Фильтр: "${title}" — ${verdict.reasons.join(", ")}`);
      filtered++;
      return;
    }
    found.push({
      salary,
      format,
      stack,
      text: snippets,
      title: normalizeText(titleEl?.textContent) || `Вакансия #${idx + 1}`,
      company: normalizeText(qs(SELECTORS.companyName, card)?.textContent) || "Неизвестная компания",
      link,
      applyBtn,
      card,
    });
  });
  if (filtered) logger.log(`Отфильтровано правилами: ${filtered}`, "warn");
  return found;
}
