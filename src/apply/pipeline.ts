import { DELAYS, pause } from "../core/delays";
import { SELECTORS } from "../core/selectors";
import type { AppState, Vacancy } from "../core/state";
import { qs } from "../dom/query";
import type { Logger } from "../infra/logger";
import type { ResumeSelector } from "../resume/selector";
import type { LetterProvider } from "../letters/letter-provider";
import { fillQuestionnaire } from "../questions/fill";
import { waitForApplyOutcome, type ApplyOutcome } from "./outcome-detector";
import { attachLetterAndSubmit } from "./submit";

export type ApplyResult = "success" | "outline" | "skipped" | "disabled" | "unclear" | "failed" | "dry_run";

export interface PipelineDeps {
  state: AppState;
  logger: Logger;
  letters: LetterProvider;
  resumes: ResumeSelector;
  onApplied: (vacancy: Vacancy) => Promise<void>;
  onOutline: (vacancy: Vacancy) => Promise<void>;
  onChange: () => Promise<void>;
  // Пробный режим: всё, кроме нажатия «Отправить»
  dryRun: () => boolean;
}

// Отклик на странице /applicant/vacancy_response (режим «из очереди»): форма уже открыта
export class ApplyPipeline {
  constructor(private deps: PipelineDeps) {}

  async applyOnPage(vacancy: Vacancy): Promise<ApplyResult> {
    const outcome = await waitForApplyOutcome(vacancy);
    return this.resolve(vacancy, outcome, 0);
  }

  private async resolve(vacancy: Vacancy, outcome: ApplyOutcome, depth: number): Promise<ApplyResult> {
    const { state, logger } = this.deps;
    logger.log(`Исход отклика: ${outcome}`);

    // Анкета с ответами backend заполняется ниже, после выбора резюме; без ответов — в «Аутлайн»
    const questionnaire = outcome === "outline";
    if (questionnaire && !vacancy.answers?.length) return this.saveOutline(vacancy);
    if (outcome === "responded") {
      await this.deps.onApplied(vacancy);
      return "success";
    }
    if (outcome === "none") {
      logger.log("Форма отклика не появилась, пропускаю", "warn");
      state.skipped++;
      return "skipped";
    }

    const chosen = await this.deps.resumes.choose(vacancy);
    if (chosen.failed) {
      qs<HTMLElement>(SELECTORS.responseModalClose)?.click();
      state.skipped++;
      return "failed";
    }
    if (questionnaire) {
      const filled = fillQuestionnaire(vacancy.answers ?? []);
      if (!filled.ok) {
        // Анкета на странице не совпала с ответами (работодатель её поменял) — не отправляем
        logger.log(`Анкета: нет ответа на ${filled.missing.length} вопр.: ${filled.missing.join(" | ").slice(0, 200)}`, "warn");
        return this.saveOutline(vacancy);
      }
      logger.log("Анкета заполнена ответами с сервера", "success");
      await pause(DELAYS.small);
    }
    const letter = await this.deps.letters.getLetter(vacancy);
    const result = await attachLetterAndSubmit(vacancy, letter, logger, this.deps.dryRun(), questionnaire);
    switch (result) {
      case "outline":
        return this.saveOutline(vacancy);
      case "success":
        await this.deps.onApplied(vacancy);
        return "success";
      case "disabled":
        return "disabled";
      case "dry_run":
        return "dry_run";
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

  // Очередь сама решает, куда идти дальше: вопросы анкеты — на сервер, затем следующая вакансия
  private async saveOutline(vacancy: Vacancy): Promise<ApplyResult> {
    const { state, logger } = this.deps;
    if (state.addOutline(this.item(vacancy))) logger.log(`Аутлайн: "${vacancy.title}"`, "warn");
    await this.deps.onOutline(vacancy);
    await this.deps.onChange();
    return "outline";
  }
}
