import { DELAYS, jitter, pause, wait } from "../core/delays";
import { saveConfig } from "../core/config";
import { nextEmptyPageAction } from "../core/retry-policy";
import { SELECTORS } from "../core/selectors";
import type { AppState } from "../core/state";
import type { PageType } from "../dom/page-type";
import { qsa } from "../dom/query";
import { collectVacancies } from "../search/vacancy-collector";
import { pageOf, reportDailyLimit, stopAuto, stopQueue, type PageStep, type StepContext } from "./step-context";

// Выдача hh.ru: сбор карточек, «Next», авто-режим и повторы пустой выдачи
export class SearchStep implements PageStep {
  readonly name = "выдача";

  private busy = false;

  constructor(private ctx: StepContext) {}

  accepts(page: PageType, state: AppState): boolean {
    return state.running && page === "search";
  }

  // Авто-режим после перезагрузки выдачи: собрать и продолжить, пустая выдача — повторы по политике
  async resume(): Promise<void> {
    const { state, logger, nav } = this.ctx;
    state.resumeAfterRedirect = false;
    logger.log("Возобновляю авто-режим после перезагрузки", "step");

    if ((await this.waitForCards()) && (await this.collect())) {
      state.resetEmptyCounters();
      await this.ctx.persist();
      await pause(DELAYS.step);
      void this.next();
      return;
    }

    const action = nextEmptyPageAction(state);
    if (action.kind === "stop") {
      logger.log("Выдача пуста после всех повторов. Авто-режим остановлен", "error");
      stopAuto(this.ctx);
      state.resetEmptyCounters();
      await this.ctx.persist();
      return;
    }
    if (action.kind === "retry") {
      state.retryDelay = action.delayMs;
      state.emptyPageRetries++;
    } else {
      state.emptyPageRetries = 0;
      state.emptyPageRestarts++;
    }
    const delay = jitter(action.delayMs);
    logger.log(`Пустая выдача, повтор через ${(delay / 1000).toFixed(1)} с`, "warn");
    await this.ctx.persist();
    await wait(delay);
    nav.reload();
  }

  async collect(): Promise<boolean> {
    const { state, logger, panel, config, storage, nav } = this.ctx;
    if (pageOf(this.ctx) !== "search") {
      logger.log("Вы не на странице поиска вакансий", "error");
      return false;
    }
    // Текущий поиск становится адресом возврата после аутлайна
    config.searchRedirectUrl = nav.url();
    panel.setUrl(nav.url());
    await saveConfig(storage, config);

    state.vacancies = collectVacancies(state, logger, config.rules);
    state.index = 0;
    await this.ctx.persist();
    logger.log(`Найдено вакансий: ${state.vacancies.length}`, "success");
    for (const v of state.vacancies.slice(0, 5))
      logger.log(`${v.title} | ${v.company} | ${v.salary || "—"} | ${v.format || "—"} | ${(v.stack ?? []).join(", ") || "—"}`);

    panel.setNextEnabled(state.vacancies.length > 0);
    panel.setStep(state.vacancies.length ? `Найдено ${state.vacancies.length}. Нажмите Next` : "Подходящих вакансий нет");
    return state.vacancies.length > 0;
  }

  async next(): Promise<void> {
    if (this.busy)
      return;

    const { state, limit, panel, logger, captcha, pipeline } = this.ctx;
    if (await limit.isReached()) {
      logger.log(`Достигнут дневной лимит (${limit.limit})`, "error");
      panel.setStep("Дневной лимит исчерпан");
      panel.setNextEnabled(false);
      stopAuto(this.ctx);
      reportDailyLimit(this.ctx);
      await this.ctx.persist();
      return;
    }

    const vacancy = state.vacancies[state.index];
    if (!vacancy) {
      panel.setStep("Готово: все вакансии обработаны");
      panel.setNextEnabled(false);
      stopAuto(this.ctx);
      await this.ctx.persist();
      return;
    }

    if (await captcha.waitUntilClear())
      panel.setStep("Капча решена, продолжаю");
    captcha.consumeHit();
    this.busy = true;
    panel.setNextEnabled(false);
    panel.setStep(`Обработка: "${vacancy.title}"`);
    let navigated = false;
    try {
      const result = await pipeline.applyFromSearch(vacancy);
      navigated = pageOf(this.ctx) !== "search";
      if (result === "disabled") {
        panel.setStep('Кнопка "Отправить" неактивна. Выберите резюме вручную и нажмите Next.');
        stopAuto(this.ctx);
      } else if (captcha.consumeHit() && !navigated) {
        // Капча сорвала шаг: повторяем ту же вакансию после её решения
        logger.log(`Капча во время "${vacancy.title}", повторю после решения`, "warn");
      } else {
        state.index++;
        panel.setStep(`${result}: "${vacancy.title}" (${state.index}/${state.vacancies.length})`);
      }
    } catch (e) {
      logger.log(`Ошибка: ${(e as Error).message}`, "error");
      stopAuto(this.ctx);
    } finally {
      this.busy = false;
      await this.ctx.persist();
      panel.setNextEnabled(state.index < state.vacancies.length);
    }

    if (state.running && !navigated) {
      await pause(DELAYS.step);
      if (state.running)
        void this.next();
    }
  }

  async toggleAuto(): Promise<void> {
    const { state, panel } = this.ctx;
    if (!state.running && state.queueRunning)
      stopQueue(this.ctx, "Режим «из очереди» остановлен: включён обычный авто-режим");

    state.running = !state.running;
    panel.setAutoActive(state.running);
    state.resetEmptyCounters();
    await this.ctx.persist();
    if (!state.running)
      return;

    if (!state.vacancies.length && !(await this.collect())) {
      stopAuto(this.ctx);
      await this.ctx.persist();
      return;
    }
    void this.next();
  }

  private async waitForCards(timeoutMs = DELAYS.longWait): Promise<boolean> {
    const start = Date.now();
    while (Date.now() - start < timeoutMs) {
      if (qsa(SELECTORS.vacancyCard).length)
        return true;
      await wait(DELAYS.small);
    }
    return false;
  }
}
