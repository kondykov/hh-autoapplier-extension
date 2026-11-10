import { DELAYS, pause } from "../core/delays";
import { SELECTORS } from "../core/selectors";
import type { Vacancy } from "../core/state";
import { normalizeText, qsa } from "../dom/query";
import type { Logger } from "../infra/logger";
import { humanClick } from "../dom/input";

const trigger = () =>
  qsa<HTMLElement>(SELECTORS.resumeTitle)
    .map((t) => t.closest<HTMLElement>(SELECTORS.resumeTrigger))
    .find(Boolean) ?? null;

// В списке hh.ru к названию резюме приклеена зарплата: «…developer130 000 ₽» (живая страница 2026-10-24)
const SALARY_SUFFIX = /\s*\d[\d\s]*[₽$€₸]$/;

export const resumeName = (text: string | null | undefined) => normalizeText(text).replace(SALARY_SUFFIX, "");

const currentName = () => resumeName(document.querySelector(SELECTORS.resumeTitle)?.textContent);

type Fits = (name: string) => boolean;

// Правило выбора (#53): единственное точное совпадение, иначе единственное по подстроке; иначе null — наугад не выбирать
export function resumeRule(names: string[], match: string): Fits | null {
  const exact: Fits = (name) => name === match;
  const partial: Fits = (name) => name.includes(match);
  return [exact, partial].find((fits) => names.filter(fits).length === 1) ?? null;
}

// failed: нужное резюме задано, но выбрать его не удалось — отправлять отклик нельзя
export interface ChooseResult {
  failed: boolean;
}

// Выбор резюме в форме отклика. Какое резюме — решает backend (#27): vacancy.resumeMatch — часть его названия на hh.ru или название целиком
export class ResumeSelector {
  constructor(private logger: Logger) {}

  async choose(vacancy: Vacancy): Promise<ChooseResult> {
    const match = normalizeText(vacancy.resumeMatch);
    if (!match) {
      this.logger.log("Сервер не выбрал резюме, оставляю выбранное hh.ru", "warn");
      return { failed: false };
    }
    // Текущее подходит только точно; по подстроке нужен список: match может подходить и к другим резюме
    if (currentName() === match) return this.chosen(vacancy);

    const opener = trigger();
    if (!opener) {
      this.logger.log("Выбор резюме недоступен (резюме одно)", "warn");
      return { failed: false };
    }
    await humanClick(opener);
    await pause(DELAYS.animation);
    const options = qsa<HTMLElement>(SELECTORS.resumeOption);
    const names = options.map((o) => resumeName(o.textContent));
    const fits = resumeRule(names, match);
    if (!fits) {
      const count = names.filter((name) => name.includes(match)).length;
      const problem = count > 1 ? `подходит несколько (${count})` : "нет совпадения";
      this.logger.log(`Резюме «${match}»: ${problem} в списке (${names.join(" | ").slice(0, 200)}) — проверьте match профиля на сервере`, "error");
      document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
      return { failed: true };
    }
    await humanClick(options[names.findIndex(fits)]);
    await pause(DELAYS.animation);

    if (!fits(currentName())) {
      this.logger.log(`Не удалось выбрать резюме «${match}»`, "error");
      return { failed: true };
    }
    return this.chosen(vacancy);
  }

  private chosen(vacancy: Vacancy): ChooseResult {
    this.logger.log(`Резюме: ${vacancy.resume ?? vacancy.resumeMatch}`, "success");
    return { failed: false };
  }
}
