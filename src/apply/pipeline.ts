import { DELAYS, pause, wait } from "../core/delays";
import { SELECTORS } from "../core/selectors";
import type { AppState, Vacancy } from "../core/state";
import { qs } from "../dom/query";
import type { Logger } from "../infra/logger";
import type { ResumeSelector } from "../resume/selector";
import type { LetterProvider } from "../letters/letter-provider";
import { fillQuestionnaire } from "../questions/fill";
import { isAlreadyRespondedOnPage, isOutlineDetected, waitForApplyOutcome, type ApplyOutcome } from "./outcome-detector";
import { attachLetterAndSubmit } from "./submit";
import { humanClick } from "../dom/input";

// stopped — очередь остановили (или перезапустили с другой вакансией), пока конвейер работал: ничего не отправлено
export type ApplyResult = "success" | "outline" | "skipped" | "disabled" | "unclear" | "failed" | "dry_run" | "stopped";

export interface PipelineDeps {
  state: AppState;
  logger: Logger;
  letters: LetterProvider;
  resumes: ResumeSelector;
  onApplied: (vacancy: Vacancy) => Promise<void>;
  onChange: () => Promise<void>;
  // Пробный режим: всё, кроме нажатия «Отправить»
  dryRun: () => boolean;
  // Очередь всё ещё ведёт эту вакансию: проверяется перед каждым шагом и перед нажатием «Отправить»
  active: (vacancy: Vacancy) => boolean;
}

// Отклик на странице /applicant/vacancy_response (режим «из очереди»): форма уже открыта
export class ApplyPipeline {
  constructor(private deps: PipelineDeps) {}

  async applyOnPage(vacancy: Vacancy): Promise<ApplyResult> {
    const outcome = await waitForApplyOutcome(vacancy);
    return this.resolve(vacancy, outcome);
  }

  private async resolve(vacancy: Vacancy, outcome: ApplyOutcome): Promise<ApplyResult> {
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

    if (!this.deps.active(vacancy)) return this.stopped();
    const chosen = await this.deps.resumes.choose(vacancy);
    if (chosen.failed) {
      const close = qs<HTMLElement>(SELECTORS.responseModalClose);
      if (close) await humanClick(close);
      state.skipped++;
      return "failed";
    }
    if (!this.deps.active(vacancy)) return this.stopped();
    if (questionnaire) {
      const filled = await fillQuestionnaire(vacancy.answers ?? []);
      if (!filled.ok) {
        // Анкета на странице не совпала с ответами (работодатель её поменял) — не отправляем
        logger.log(`Анкета: нет ответа на ${filled.missing.length} вопр.: ${filled.missing.join(" | ").slice(0, 200)}`, "warn");
        return this.saveOutline(vacancy);
      }
      logger.log("Анкета заполнена ответами с сервера", "success");
      await pause(DELAYS.small);
    }
    const letter = await this.deps.letters.getLetter(vacancy);
    if (!this.deps.active(vacancy)) return this.stopped();
    // Перечитывается перед самым кликом: остановка очереди за время паузы — не отправлять
    const dryRun = () => this.deps.dryRun() || !this.deps.active(vacancy);
    const result = await attachLetterAndSubmit(vacancy, letter, logger, dryRun, questionnaire);
    if (result === "dry_run" && !this.deps.active(vacancy)) return this.stopped();
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
        return this.afterPageChange(vacancy);
      case "unclear":
        logger.log('Отклик не подтверждён, добавляю в "Непонятное"', "warn");
        state.skipped++;
        return "unclear";
      default:
        state.skipped++;
        return "failed";
    }
  }

  // «Отправить» уже нажата, и hh.ru сменил страницу: второй отправки не бывает (#50) — только исход новой страницы.
  // Анкета — вопросы на сервер; «Вы откликнулись» — отклик; иначе отклик мог уйти — «Непонятное»
  private async afterPageChange(vacancy: Vacancy, timeoutMs = 8000): Promise<ApplyResult> {
    this.deps.logger.log("После отправки страница сменилась — жду исход без повторного нажатия");
    const start = Date.now();
    while (Date.now() - start < timeoutMs) {
      if (isOutlineDetected()) return this.saveOutline(vacancy);
      if (isAlreadyRespondedOnPage()) {
        await this.deps.onApplied(vacancy);
        return "success";
      }
      await wait(300);
    }
    this.deps.logger.log('Отклик не подтверждён, добавляю в "Непонятное"', "warn");
    this.deps.state.skipped++;
    return "unclear";
  }

  private stopped(): ApplyResult {
    this.deps.logger.log("Очередь остановлена — отклик не отправлен", "warn");
    return "stopped";
  }

  // Очередь сама решает, куда идти дальше: вопросы анкеты — на сервер, затем следующая вакансия
  private async saveOutline(vacancy: Vacancy): Promise<ApplyResult> {
    this.deps.logger.log(`Анкета без ответов: "${vacancy.title}"`, "warn");
    await this.deps.onChange();
    return "outline";
  }
}
