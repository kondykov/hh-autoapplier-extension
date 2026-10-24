import { DELAYS, wait } from "../core/delays";
import type { AppState, Vacancy } from "../core/state";
import type { PageType } from "../dom/page-type";
import { extractVacancyInfo } from "./vacancy-info";
import { pageOf, stopAuto, type PageStep, type StepContext } from "./step-context";

// Страница вакансии или отклика в авто-режиме (сюда приводит переход с выдачи): отклик и возврат на поиск
export class VacancyStep implements PageStep {
  readonly name = "страница вакансии";

  constructor(private ctx: StepContext) {}

  accepts(page: PageType, state: AppState): boolean {
    return state.running && (page === "vacancy" || page === "response");
  }

  async resume(): Promise<void> {
    const { state, logger, panel, captcha, limit, pipeline, nav, config } = this.ctx;
    await captcha.waitUntilClear();
    if (await limit.isReached()) {
      stopAuto(this.ctx);
      await this.ctx.persist();
      return;
    }

    const info = extractVacancyInfo();
    const vacancy: Vacancy = { ...info, link: nav.url(), card: null, applyBtn: null };
    panel.setStep(`Страница вакансии: "${info.title}"`);
    try {
      const result = await pipeline.applyOnPage(vacancy);
      if (result === "disabled") {
        panel.setStep('Кнопка "Отправить" неактивна. Выберите резюме вручную.');
        stopAuto(this.ctx);
        await this.ctx.persist();
        return;
      }
    } catch (e) {
      logger.log(`Ошибка: ${(e as Error).message}`, "error");
      stopAuto(this.ctx);
      await this.ctx.persist();
      return;
    }

    // На анкете конвейер уже сам вернул на поиск
    if (pageOf(this.ctx) === "search")
      return;

    state.resumeAfterRedirect = true;
    await this.ctx.persist();
    await wait(DELAYS.step);
    nav.go(config.searchRedirectUrl);
  }
}
