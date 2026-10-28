import { DELAYS, pause } from "../core/delays";
import { SELECTORS } from "../core/selectors";
import type { Vacancy } from "../core/state";
import { normalizeText, qsa } from "../dom/query";
import type { Logger } from "../infra/logger";
import { humanClick } from "../dom/input";

const currentTitle = () => normalizeText(document.querySelector(SELECTORS.resumeTitle)?.textContent);

const trigger = () =>
  qsa<HTMLElement>(SELECTORS.resumeTitle)
    .map((t) => t.closest<HTMLElement>(SELECTORS.resumeTrigger))
    .find(Boolean) ?? null;

// failed: нужное резюме задано, но выбрать его не удалось — отправлять отклик нельзя
export interface ChooseResult {
  failed: boolean;
}

// Выбор резюме в форме отклика. Какое резюме — решает backend (#27): vacancy.resumeMatch — подстрока его названия на hh.ru
export class ResumeSelector {
  constructor(private logger: Logger) {}

  async choose(vacancy: Vacancy): Promise<ChooseResult> {
    const match = vacancy.resumeMatch?.trim();
    if (!match) {
      this.logger.log("Сервер не выбрал резюме, оставляю выбранное hh.ru", "warn");
      return { failed: false };
    }
    if (currentTitle().includes(match)) return this.chosen(vacancy);

    const opener = trigger();
    if (!opener) {
      this.logger.log("Выбор резюме недоступен (резюме одно)", "warn");
      return { failed: false };
    }
    await humanClick(opener);
    await pause(DELAYS.animation);
    const option = qsa<HTMLElement>(SELECTORS.resumeOption).find((o) => normalizeText(o.textContent).includes(match));
    if (!option) {
      this.logger.log(`Резюме «${match}» не найдено в списке`, "error");
      document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
      return { failed: true };
    }
    await humanClick(option);
    await pause(DELAYS.animation);

    if (!currentTitle().includes(match)) {
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
