import { ApplyPipeline } from "../apply/pipeline";
import { DELAYS, wait } from "../core/delays";
import { saveConfig, type AppConfig } from "../core/config";
import { DailyLimit } from "../core/daily-limit";
import { StateRepository, type AppState } from "../core/state";
import { detectPageType } from "../dom/page-type";
import type { KeyValueStorage } from "../infra/storage";
import type { Logger } from "../infra/logger";
import { collectVacancies } from "../search/vacancy-collector";
import { showListModal } from "../ui/modal";
import type { Panel } from "../ui/panel";

interface ControllerDeps {
  storage: KeyValueStorage;
  config: AppConfig;
  state: AppState;
  repo: StateRepository;
  limit: DailyLimit;
  logger: Logger;
  panel: Panel;
}

export class Controller {
  private auto = false;
  private busy = false;
  private pipeline: ApplyPipeline;

  constructor(private d: ControllerDeps) {
    this.pipeline = new ApplyPipeline({
      state: d.state,
      logger: d.logger,
      letter: () => d.config.coverLetterTemplate,
      searchUrl: () => d.config.searchRedirectUrl,
      onApplied: async (title) => {
        await d.limit.increment();
        d.state.applied++;
        d.logger.log(`Отклик зарегистрирован: "${title}"`, "success");
      },
      onChange: () => this.persist(),
    });
  }

  async init(): Promise<void> {
    await this.refresh();
    this.d.panel.setStep(
      detectPageType() === "search"
        ? 'Нажмите "Собрать вакансии"'
        : "Откройте страницу поиска вакансий (/search/vacancy)",
    );
  }

  private async persist() {
    await this.d.repo.save(this.d.state);
    await this.refresh();
  }

  private async refresh() {
    const { state, limit, panel } = this.d;
    panel.setStats({
      applied: state.applied,
      outline: state.outline.length,
      unclear: state.unclear.length,
      total: state.vacancies.length,
      daily: await limit.count(),
      dailyLimit: limit.limit,
    });
  }

  async collect(): Promise<void> {
    const { state, logger, panel, config, storage } = this.d;
    if (detectPageType() !== "search") {
      logger.log("Вы не на странице поиска вакансий", "error");
      return;
    }
    // Текущий поиск становится адресом возврата после аутлайна
    config.searchRedirectUrl = location.href;
    panel.setUrl(location.href);
    await saveConfig(storage, config);

    state.vacancies = collectVacancies(state, logger);
    state.index = 0;
    await this.persist();
    logger.log(`Найдено вакансий: ${state.vacancies.length}`, "success");
    panel.setNextEnabled(state.vacancies.length > 0);
    panel.setStep(
      state.vacancies.length ? `Найдено ${state.vacancies.length}. Нажмите Next` : "Подходящих вакансий нет",
    );
  }

  async next(): Promise<void> {
    if (this.busy) return;
    const { state, limit, panel, logger } = this.d;

    if (await limit.isReached()) {
      logger.log(`Достигнут дневной лимит (${limit.limit})`, "error");
      panel.setStep("Дневной лимит исчерпан");
      panel.setNextEnabled(false);
      this.stopAuto();
      return;
    }
    const vacancy = state.vacancies[state.index];
    if (!vacancy) {
      panel.setStep("Готово: все вакансии обработаны");
      panel.setNextEnabled(false);
      this.stopAuto();
      return;
    }

    this.busy = true;
    panel.setNextEnabled(false);
    panel.setStep(`Обработка: "${vacancy.title}"`);
    try {
      const result = await this.pipeline.applyFromSearch(vacancy);
      if (result === "disabled") {
        panel.setStep('Кнопка "Отправить" неактивна. Выберите резюме вручную и нажмите Next.');
        this.stopAuto();
      } else {
        state.index++;
        panel.setStep(`${result}: "${vacancy.title}" (${state.index}/${state.vacancies.length})`);
      }
    } catch (e) {
      this.d.logger.log(`Ошибка: ${(e as Error).message}`, "error");
      this.stopAuto();
    } finally {
      this.busy = false;
      await this.persist();
      panel.setNextEnabled(state.index < state.vacancies.length);
    }

    if (this.auto) {
      await wait(DELAYS.step);
      if (this.auto) void this.next();
    }
  }

  toggleAuto(): void {
    this.auto = !this.auto;
    this.d.panel.setAutoActive(this.auto);
    if (this.auto) void this.next();
  }

  private stopAuto() {
    this.auto = false;
    this.d.panel.setAutoActive(false);
  }

  showOutline() {
    showListModal("Аутлайн", this.d.state.outline);
  }

  showUnclear() {
    showListModal("Непонятное", this.d.state.unclear);
  }

  async clear(): Promise<void> {
    this.d.state.clearOutline();
    await this.persist();
    this.d.logger.log("Аутлайн и список пропусков очищены", "success");
  }

  async setUrl(url: string) {
    this.d.config.searchRedirectUrl = url;
    await saveConfig(this.d.storage, this.d.config);
  }

  async setLimit(limit: number) {
    this.d.config.dailyLimit = limit;
    this.d.limit.limit = limit;
    await saveConfig(this.d.storage, this.d.config);
    await this.refresh();
  }
}
