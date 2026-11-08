import { DELAYS, pause } from "../core/delays";
import { SELECTORS } from "../core/selectors";
import type { Vacancy } from "../core/state";
import { normalizeText, qsa } from "../dom/query";
import type { Logger } from "../infra/logger";
import { humanClick } from "../dom/input";

const currentTitle = () => document.querySelector(SELECTORS.resumeTitle)?.textContent;

const trigger = () =>
  qsa<HTMLElement>(SELECTORS.resumeTitle)
    .map((t) => t.closest<HTMLElement>(SELECTORS.resumeTrigger))
    .find(Boolean) ?? null;

// В списке hh.ru к названию резюме приклеена зарплата: «…developer130 000 ₽» (живая страница 2026-10-24)
const SALARY_SUFFIX = /\s*\d[\d\s]*[₽$€₸]$/;

// Название резюме целиком: подстрока «Backend» не должна подойти и к «Backend .NET», и к «Senior Backend» (#53)
export function isSameResume(text: string | null | undefined, match: string): boolean {
  const title = normalizeText(text);
  return title === match || title.replace(SALARY_SUFFIX, "") === match;
}

// failed: нужное резюме задано, но выбрать его не удалось — отправлять отклик нельзя
export interface ChooseResult {
  failed: boolean;
}

// Выбор резюме в форме отклика. Какое резюме — решает backend (#27): vacancy.resumeMatch — полное название резюме на hh.ru
export class ResumeSelector {
  constructor(private logger: Logger) {}

  async choose(vacancy: Vacancy): Promise<ChooseResult> {
    const match = normalizeText(vacancy.resumeMatch);
    if (!match) {
      this.logger.log("Сервер не выбрал резюме, оставляю выбранное hh.ru", "warn");
      return { failed: false };
    }
    if (isSameResume(currentTitle(), match)) return this.chosen(vacancy);

    const opener = trigger();
    if (!opener) {
      this.logger.log("Выбор резюме недоступен (резюме одно)", "warn");
      return { failed: false };
    }
    await humanClick(opener);
    await pause(DELAYS.animation);
    const options = qsa<HTMLElement>(SELECTORS.resumeOption);
    const found = options.filter((o) => isSameResume(o.textContent, match));
    if (found.length !== 1) {
      const problem = found.length > 1 ? `подходит несколько (${found.length})` : "нет точного совпадения";
      const titles = options.map((o) => normalizeText(o.textContent)).join(" | ").slice(0, 200);
      this.logger.log(`Резюме «${match}»: ${problem} в списке (${titles}) — проверьте match профиля на сервере`, "error");
      document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
      return { failed: true };
    }
    await humanClick(found[0]);
    await pause(DELAYS.animation);

    if (!isSameResume(currentTitle(), match)) {
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
