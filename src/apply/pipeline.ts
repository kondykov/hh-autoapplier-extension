import { DELAYS, wait } from "../core/delays";
import { SELECTORS } from "../core/selectors";
import type { AppState, Vacancy } from "../core/state";
import { detectPageType } from "../dom/page-type";
import { qs } from "../dom/query";
import type { Logger } from "../infra/logger";
import { waitForApplyOutcome, type ApplyOutcome } from "./outcome-detector";
import { attachLetterAndSubmit } from "./submit";

export type ApplyResult = "success" | "outline" | "skipped" | "disabled" | "unclear" | "failed";

export interface PipelineDeps {
  state: AppState;
  logger: Logger;
  letter: () => string;
  searchUrl: () => string;
  onApplied: (title: string) => Promise<void>;
  onChange: () => Promise<void>;
}

async function handleRelocationWarning(logger: Logger): Promise<void> {
  for (let i = 0; i < 5; i++) {
    await wait(400);
    const btn = qs<HTMLElement>(SELECTORS.relocationConfirmBtn);
    if (btn) {
      logger.log('Окно "отклик в другую страну": подтверждаю', "warn");
      btn.click();
      await wait(DELAYS.wait);
      return;
    }
    if (qs(SELECTORS.responseModal)) return;
  }
}

export class ApplyPipeline {
  constructor(private deps: PipelineDeps) {}

  async applyFromSearch(vacancy: Vacancy): Promise<ApplyResult> {
    const { logger } = this.deps;
    if (!vacancy.applyBtn) {
      logger.log("Кнопка отклика не найдена, пропускаю", "warn");
      this.deps.state.skipped++;
      return "skipped";
    }
    logger.log(`Отклик: "${vacancy.title}"`, "step");
    vacancy.applyBtn.scrollIntoView({ behavior: "smooth", block: "center" });
    await wait(DELAYS.click);
    vacancy.applyBtn.click();

    await handleRelocationWarning(logger);
    await wait(DELAYS.animation);
    const outcome = await waitForApplyOutcome(vacancy);
    return this.resolve(vacancy, outcome, 0);
  }

  private async resolve(vacancy: Vacancy, outcome: ApplyOutcome, depth: number): Promise<ApplyResult> {
    const { state, logger } = this.deps;
    logger.log(`Исход отклика: ${outcome}`);

    if (outcome === "outline") return this.saveOutline(vacancy);
    if (outcome === "responded") {
      await this.deps.onApplied(vacancy.title);
      return "success";
    }
    if (outcome === "none") {
      logger.log("После клика ничего не появилось, пропускаю", "warn");
      state.skipped++;
      return "skipped";
    }

    const result = await attachLetterAndSubmit(vacancy, this.deps.letter(), logger);
    switch (result) {
      case "outline":
        return this.saveOutline(vacancy);
      case "success":
        await this.deps.onApplied(vacancy.title);
        return "success";
      case "disabled":
        return "disabled";
      case "pending_response":
        if (depth < 2) return this.resolve(vacancy, "response", depth + 1);
        return "failed";
      case "unclear":
        logger.log('Отклик не подтверждён, добавляю в "Непонятное"', "warn");
        state.addUnclear(this.item(vacancy));
        state.skipped++;
        return "unclear";
      default:
        state.skipped++;
        return "failed";
    }
  }

  private item(vacancy: Vacancy) {
    return { title: vacancy.title, link: vacancy.link || location.href, company: vacancy.company };
  }

  private async saveOutline(vacancy: Vacancy): Promise<ApplyResult> {
    const { state, logger } = this.deps;
    if (state.addOutline(this.item(vacancy))) logger.log(`Аутлайн: "${vacancy.title}"`, "warn");
    await this.deps.onChange();

    if (detectPageType() === "search") {
      qs<HTMLElement>(SELECTORS.responseModalClose)?.click();
    } else {
      location.href = this.deps.searchUrl();
    }
    return "outline";
  }
}
