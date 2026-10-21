import { CaptchaGuard } from "../captcha/guard";
import { ApplyPipeline } from "../apply/pipeline";
import { DELAYS, wait } from "./delays";
import { saveConfig, type AppConfig } from "./config";
import { DailyLimit } from "./daily-limit";
import { nextEmptyPageAction } from "./retry-policy";
import { StateRepository, type AppState, type Vacancy } from "./state";
import { detectPageType } from "../dom/page-type";
import { qsa } from "../dom/query";
import type { KeyValueStorage } from "../infra/storage";
import type { Logger } from "../infra/logger";
import type { LetterProvider } from "../letters/letter-provider";
import { extractVacancyInfo } from "../pages/vacancy-info";
import { downloadJson, QuestionStore } from "../questions/store";
import type { ResumeProfile } from "../resume/profiles";
import type { FilterRules } from "../filter/rules";
import type { ResumeSelector } from "../resume/selector";
import { ApplicationStore, downloadCsv, toCsv } from "../report/applications";
import { extractQuestions } from "../questions/extract";
import { openNextRejection } from "../rejects/runner";
import { collectVacancies } from "../search/vacancy-collector";
import { SELECTORS } from "./selectors";
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
  letters: LetterProvider;
  questions: QuestionStore;
  applications: ApplicationStore;
  resumes: ResumeSelector;
}

export class Controller {
  private busy = false;
  private rejecting = false;
  private pipeline: ApplyPipeline;
  private captcha: CaptchaGuard;

  constructor(private d: ControllerDeps) {
    this.captcha = new CaptchaGuard((shown) => {
      d.panel.setCaptcha(shown);
      d.logger.log(shown ? "Капча: авто-режим на паузе" : "Капча решена, продолжаю", shown ? "warn" : "success");
    });
    this.pipeline = new ApplyPipeline({
      state: d.state,
      logger: d.logger,
      letters: d.letters,
      resumes: d.resumes,
      searchUrl: () => d.config.searchRedirectUrl,
      onApplied: async (vacancy) => {
        await d.limit.increment();
        d.state.applied++;
        await d.applications.add({
          title: vacancy.title,
          stack: (vacancy.stack ?? []).join(", "),
          company: vacancy.company,
          salary: vacancy.salary ?? "",
          format: vacancy.format ?? "",
          resume: vacancy.resume ?? "",
          link: vacancy.link || location.href,
          appliedAt: new Date().toISOString(),
        });
        d.logger.log(`Отклик зарегистрирован: "${vacancy.title}"`, "success");
      },
      onOutline: (vacancy) => this.saveQuestions(vacancy),
      onChange: () => this.persist(),
    });
  }

  async init(): Promise<void> {
    const { state, panel } = this.d;
    await this.refresh();
    this.captcha.start();
    panel.setAutoActive(state.running);
    const page = detectPageType();
    panel.setStep(
      page === "search" ? 'Нажмите "Собрать вакансии"' : "Откройте страницу поиска вакансий (/search/vacancy)",
    );
    if (!state.running) return;
    await this.captcha.waitUntilClear();

    if (page === "search") await this.resumeOnSearch();
    else if (page === "response" || page === "vacancy") await this.handleVacancyPage();
  }

  private async persist() {
    await this.d.repo.save(this.d.state);
    await this.refresh();
  }

  private async refresh() {
    const { state, limit, panel, questions } = this.d;
    panel.setStats({
      applied: state.applied,
      outline: state.outline.length,
      unclear: state.unclear.length,
      questions: (await questions.list()).length,
      total: state.vacancies.length,
      daily: await limit.count(),
      dailyLimit: limit.limit,
    });
  }

  private async saveQuestions(vacancy: Vacancy) {
    const answers = extractQuestions();
    if (!answers.length) return;
    const url = vacancy.link || location.href;
    const added = await this.d.questions.add({
      vacancyTitle: vacancy.title,
      vacancyUrl: url,
      collectedAt: new Date().toISOString(),
      answers,
    });
    if (added) this.d.logger.log(`Сохранено вопросов: ${answers.length}`, "success");
  }

  async downloadQuestions(): Promise<void> {
    const all = await this.d.questions.list();
    if (!all.length) {
      this.d.logger.log("Нет собранных вопросов", "warn");
      return;
    }
    downloadJson(`collected_questions_${new Date().toISOString().slice(0, 10)}.json`, all);
    this.d.logger.log(`Скачано анкет: ${all.length}`, "success");
  }

  async downloadReport(): Promise<void> {
    const all = await this.d.applications.list();
    if (!all.length) {
      this.d.logger.log("Откликов для отчёта пока нет", "warn");
      return;
    }
    downloadCsv(`applications_${new Date().toISOString().slice(0, 10)}.csv`, toCsv(all));
    this.d.logger.log(`Отчёт: ${all.length} откликов`, "success");
  }

  async collect(): Promise<boolean> {
    const { state, logger, panel, config, storage } = this.d;
    if (detectPageType() !== "search") {
      logger.log("Вы не на странице поиска вакансий", "error");
      return false;
    }
    // Текущий поиск становится адресом возврата после аутлайна
    config.searchRedirectUrl = location.href;
    panel.setUrl(location.href);
    await saveConfig(storage, config);

    state.vacancies = collectVacancies(state, logger, config.rules);
    state.index = 0;
    await this.persist();
    logger.log(`Найдено вакансий: ${state.vacancies.length}`, "success");
    for (const v of state.vacancies.slice(0, 5)) {
      logger.log(`${v.title} | ${v.company} | ${v.salary || "—"} | ${v.format || "—"} | ${(v.stack ?? []).join(", ") || "—"}`);
    }
    panel.setNextEnabled(state.vacancies.length > 0);
    panel.setStep(
      state.vacancies.length ? `Найдено ${state.vacancies.length}. Нажмите Next` : "Подходящих вакансий нет",
    );
    return state.vacancies.length > 0;
  }

  private async waitForCards(timeoutMs = DELAYS.longWait): Promise<boolean> {
    const start = Date.now();
    while (Date.now() - start < timeoutMs) {
      if (qsa(SELECTORS.vacancyCard).length) return true;
      await wait(DELAYS.small);
    }
    return false;
  }

  private async resumeOnSearch(): Promise<void> {
    const { state, logger } = this.d;
    state.resumeAfterRedirect = false;
    logger.log("Возобновляю авто-режим после перезагрузки", "step");

    if ((await this.waitForCards()) && (await this.collect())) {
      state.resetEmptyCounters();
      await this.persist();
      await wait(DELAYS.step);
      void this.next();
      return;
    }

    const action = nextEmptyPageAction(state);
    if (action.kind === "stop") {
      logger.log("Выдача пуста после всех повторов. Авто-режим остановлен", "error");
      this.stopAuto();
      state.resetEmptyCounters();
      await this.persist();
      return;
    }
    if (action.kind === "retry") {
      state.retryDelay = action.delayMs;
      state.emptyPageRetries++;
    } else {
      state.emptyPageRetries = 0;
      state.emptyPageRestarts++;
    }
    logger.log(`Пустая выдача, повтор через ${action.delayMs / 1000} с`, "warn");
    await this.persist();
    await wait(action.delayMs);
    location.reload();
  }

  private async handleVacancyPage(): Promise<void> {
    const { state, logger, panel } = this.d;
    await this.captcha.waitUntilClear();
    if (await this.d.limit.isReached()) {
      this.stopAuto();
      await this.persist();
      return;
    }
    const info = extractVacancyInfo();
    const vacancy: Vacancy = { ...info, link: location.href, card: null, applyBtn: null };
    panel.setStep(`Страница вакансии: "${info.title}"`);
    try {
      const result = await this.pipeline.applyOnPage(vacancy);
      if (result === "disabled") {
        panel.setStep('Кнопка "Отправить" неактивна. Выберите резюме вручную.');
        this.stopAuto();
        await this.persist();
        return;
      }
    } catch (e) {
      logger.log(`Ошибка: ${(e as Error).message}`, "error");
      this.stopAuto();
      await this.persist();
      return;
    }
    // outline уже сам выполнил редирект
    if (detectPageType() !== "search") {
      state.resumeAfterRedirect = true;
      await this.persist();
      await wait(DELAYS.step);
      location.href = this.d.config.searchRedirectUrl;
    }
  }

  async next(): Promise<void> {
    if (this.busy) return;
    const { state, limit, panel, logger } = this.d;

    if (await limit.isReached()) {
      logger.log(`Достигнут дневной лимит (${limit.limit})`, "error");
      panel.setStep("Дневной лимит исчерпан");
      panel.setNextEnabled(false);
      this.stopAuto();
      await this.persist();
      return;
    }
    const vacancy = state.vacancies[state.index];
    if (!vacancy) {
      panel.setStep("Готово: все вакансии обработаны");
      panel.setNextEnabled(false);
      this.stopAuto();
      await this.persist();
      return;
    }

    if (await this.captcha.waitUntilClear()) panel.setStep("Капча решена, продолжаю");
    this.captcha.consumeHit();
    this.busy = true;
    panel.setNextEnabled(false);
    panel.setStep(`Обработка: "${vacancy.title}"`);
    let navigated = false;
    try {
      const result = await this.pipeline.applyFromSearch(vacancy);
      navigated = detectPageType() !== "search";
      if (result === "disabled") {
        panel.setStep('Кнопка "Отправить" неактивна. Выберите резюме вручную и нажмите Next.');
        this.stopAuto();
      } else if (this.captcha.consumeHit() && !navigated) {
        // Капча сорвала шаг: повторяем ту же вакансию после её решения
        logger.log(`Капча во время "${vacancy.title}", повторю после решения`, "warn");
      } else {
        state.index++;
        panel.setStep(`${result}: "${vacancy.title}" (${state.index}/${state.vacancies.length})`);
      }
    } catch (e) {
      logger.log(`Ошибка: ${(e as Error).message}`, "error");
      this.stopAuto();
    } finally {
      this.busy = false;
      await this.persist();
      panel.setNextEnabled(state.index < state.vacancies.length);
    }

    if (state.running && !navigated) {
      await wait(DELAYS.step);
      if (state.running) void this.next();
    }
  }

  async toggleAuto(): Promise<void> {
    const { state, panel } = this.d;
    state.running = !state.running;
    panel.setAutoActive(state.running);
    state.resetEmptyCounters();
    await this.persist();
    if (state.running) {
      if (!state.vacancies.length && !(await this.collect())) {
        this.stopAuto();
        await this.persist();
        return;
      }
      void this.next();
    }
  }

  private stopAuto() {
    this.d.state.running = false;
    this.d.state.resumeAfterRedirect = false;
    this.d.panel.setAutoActive(false);
  }

  async rejectNext(): Promise<boolean> {
    const { logger, panel } = this.d;
    const page = location.href;
    if (!page.includes("/chat") && !page.includes("/applicant/negotiations")) {
      logger.log("Откройте страницу чатов (/chat или /applicant/negotiations)", "error");
      return false;
    }
    const found = await openNextRejection(logger);
    if (!found) panel.setStep("Отказов больше нет");
    return found;
  }

  async toggleAutoReject(): Promise<void> {
    this.rejecting = !this.rejecting;
    this.d.panel.setRejectActive(this.rejecting);
    while (this.rejecting) {
      if (!(await this.rejectNext())) this.rejecting = false;
    }
    this.d.panel.setRejectActive(false);
  }

  showOutline() {
    showListModal("Аутлайн", this.d.state.outline);
  }

  showUnclear() {
    showListModal("Непонятное", this.d.state.unclear);
  }

  async clear(): Promise<void> {
    if (!confirm("Очистить аутлайн, непонятное и собранные вопросы?")) return;
    this.d.state.clearOutline();
    await this.d.questions.clear();
    await this.persist();
    this.d.logger.log("Аутлайн, пропуски и вопросы очищены", "success");
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

  async setLetter(text: string) {
    this.d.config.coverLetterTemplate = text;
    await saveConfig(this.d.storage, this.d.config);
  }

  async setResumes(resumes: ResumeProfile[]) {
    this.d.config.resumes = resumes;
    await saveConfig(this.d.storage, this.d.config);
  }

  async setRules(rules: FilterRules) {
    this.d.config.rules = rules;
    await saveConfig(this.d.storage, this.d.config);
  }

  async savePreset(name: string) {
    const { config, storage } = this.d;
    const url = config.searchRedirectUrl;
    config.presets = [...config.presets.filter((p) => p.name !== name), { name, url, letter: config.coverLetterTemplate, rules: config.rules }];
    await saveConfig(storage, config);
    this.d.panel.setPresets(config.presets.map((p) => p.name));
  }

  async deletePreset(name: string) {
    this.d.config.presets = this.d.config.presets.filter((p) => p.name !== name);
    await saveConfig(this.d.storage, this.d.config);
    this.d.panel.setPresets(this.d.config.presets.map((p) => p.name));
  }

  async applyPreset(name: string) {
    const preset = this.d.config.presets.find((p) => p.name === name);
    if (!preset) return;
    await this.setUrl(preset.url);
    if (preset.rules) {
      await this.setRules(preset.rules);
      this.d.panel.setRules(preset.rules);
    }
    if (preset.letter) {
      await this.setLetter(preset.letter);
      this.d.panel.setLetter(preset.letter);
    }
    this.d.panel.setUrl(preset.url);
    if (detectPageType() === "search" || confirm(`Перейти на поиск "${name}"?`)) location.href = preset.url;
  }
}
