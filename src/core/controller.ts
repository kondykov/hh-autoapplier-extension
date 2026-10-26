import type { BackendClient } from "../api/client";
import { ApplyPipeline } from "../apply/pipeline";
import { CaptchaGuard } from "../captcha/guard";
import type { KeyValueStorage } from "../infra/storage";
import type { Logger } from "../infra/logger";
import type { LetterProvider } from "../letters/letter-provider";
import { QueueStep } from "../pages/queue-step";
import { RejectStep } from "../pages/reject-step";
import { SearchStep } from "../pages/search-step";
import { browserNavigator, pageOf, pickStep, type Navigator, type StepContext } from "../pages/step-context";
import { VacancyStep } from "../pages/vacancy-step";
import type { QuestionStore } from "../questions/store";
import type { ApplicationStore } from "../report/applications";
import { ReportActions } from "../report/report-actions";
import type { ResumeSelector } from "../resume/selector";
import type { Panel } from "../ui/panel";
import type { AppConfig } from "./config";
import type { DailyLimit } from "./daily-limit";
import { Settings } from "./settings";
import type { AppState, StateRepository } from "./state";

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
  backend: BackendClient;
  nav?: Navigator;
}

// Сборка: общий контекст, шаги страниц, настройки и отчёты. После загрузки страницы управление получает подходящий шаг
export class Controller {
  readonly search: SearchStep;

  readonly vacancy: VacancyStep;

  readonly queue: QueueStep;

  readonly rejects: RejectStep;

  readonly settings: Settings;

  readonly reports: ReportActions;

  private readonly captcha: CaptchaGuard;

  private readonly ctx: StepContext;

  constructor(private d: ControllerDeps) {
    this.captcha = new CaptchaGuard((shown) => {
      d.panel.setCaptcha(shown);
      d.logger.log(shown ? "Капча: авто-режим на паузе" : "Капча решена, продолжаю", shown ? "warn" : "success");
      // Работа встала и ждёт человека — сообщить в Telegram (#28); при ручном просмотре hh.ru капча — не новость
      if (shown && (d.state.running || d.state.queueRunning))
        void d.backend.reportEvent("Captcha", location.href);
    });
    this.reports = new ReportActions(d.state, d.questions, d.applications, d.logger, () => this.persist(), (m, yes) => d.panel.ask(m, yes));
    this.settings = new Settings(d.config, d.storage, d.panel, d.logger, d.limit, d.backend, () => this.refresh());

    const ctx: StepContext = {
      state: d.state,
      config: d.config,
      storage: d.storage,
      panel: d.panel,
      logger: d.logger,
      limit: d.limit,
      backend: d.backend,
      captcha: this.captcha,
      nav: d.nav ?? browserNavigator,
      pipeline: this.createPipeline(),
      persist: () => this.persist(),
    };
    this.search = new SearchStep(ctx);
    this.vacancy = new VacancyStep(ctx);
    this.queue = new QueueStep(ctx);
    this.rejects = new RejectStep(ctx);
    this.ctx = ctx;
  }

  async init(): Promise<void> {
    const { state, panel } = this.d;
    await this.refresh();
    this.captcha.start();
    panel.setAutoActive(state.running);
    panel.setQueueActive(state.queueRunning);

    const page = pageOf(this.ctx);
    // Порядок важен: очередь берётся за любую страницу и важнее обычного авто-режима
    const step = pickStep([this.queue, this.search, this.vacancy], page, state);
    if (!step) {
      panel.setStep(page === "search" ? 'Нажмите "Собрать вакансии"' : "Откройте страницу поиска вакансий (/search/vacancy)");
      return;
    }
    if (step !== this.queue)
      await this.captcha.waitUntilClear();
    await step.resume();
  }

  private createPipeline(): ApplyPipeline {
    const d = this.d;
    return new ApplyPipeline({
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
      onOutline: (vacancy) => this.reports.saveQuestions(vacancy),
      onChange: () => this.persist(),
      dryRun: () => d.state.queueRunning && d.config.queueDryRun,
      stayOnOutline: () => d.state.queueRunning,
    });
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
}
