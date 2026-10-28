import type { BackendClient } from "../api/client";
import { ApplyPipeline } from "../apply/pipeline";
import { CaptchaGuard } from "../captcha/guard";
import type { KeyValueStorage } from "../infra/storage";
import type { Logger } from "../infra/logger";
import type { LetterProvider } from "../letters/letter-provider";
import { DialogStep } from "../pages/dialog-step";
import { QueueStep } from "../pages/queue-step";
import { RejectStep } from "../pages/reject-step";
import { SearchStep } from "../pages/search-step";
import { browserNavigator, pageOf, pickStep, type Navigator, type StepContext } from "../pages/step-context";
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
  resumes: ResumeSelector;
  backend: BackendClient;
  nav?: Navigator;
}

// Сборка: общий контекст, шаги страниц, настройки и отчёты. После загрузки страницы управление получает подходящий шаг
export class Controller {
  readonly search: SearchStep;

  readonly queue: QueueStep;

  readonly rejects: RejectStep;

  readonly dialogs: DialogStep;

  readonly settings: Settings;


  private readonly captcha: CaptchaGuard;

  private readonly ctx: StepContext;

  constructor(private d: ControllerDeps) {
    this.captcha = new CaptchaGuard((shown) => {
      d.panel.setCaptcha(shown);
      d.logger.log(shown ? "Капча: работа на паузе" : "Капча решена, продолжаю", shown ? "warn" : "success");
      // Работа встала и ждёт человека — сообщить в Telegram (#28); при ручном просмотре hh.ru капча — не новость
      if (shown && d.state.queueRunning)
        void d.backend.reportEvent("Captcha", location.href);
    });
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
    this.queue = new QueueStep(ctx);
    this.rejects = new RejectStep(ctx);
    this.dialogs = new DialogStep(ctx);
    this.ctx = ctx;
  }

  async init(): Promise<void> {
    const { state, panel } = this.d;
    await this.refresh();
    this.captcha.start();
    panel.setQueueActive(state.queueRunning);

    const page = pageOf(this.ctx);
    const step = pickStep([this.queue, this.dialogs], page, state);
    if (!step) {
      panel.setStep(page === "search" ? "Отправьте выдачу на сервер или запустите очередь"
        : page === "chat" ? "«Автоответ» — чаты на сервер, «спасибо» на новые отказы" : "Запустите режим «из очереди»");
      return;
    }
    await step.resume();
  }

  private createPipeline(): ApplyPipeline {
    const d = this.d;
    return new ApplyPipeline({
      state: d.state,
      logger: d.logger,
      letters: d.letters,
      resumes: d.resumes,
      onApplied: async (vacancy) => {
        await d.limit.increment();
        d.state.applied++;
        d.logger.log(`Отклик зарегистрирован: "${vacancy.title}"`, "success");
      },
      onChange: () => this.persist(),
      dryRun: () => d.state.queueRunning && d.config.queueDryRun,
    });
  }

  private async persist() {
    await this.d.repo.save(this.d.state);
    await this.refresh();
  }

  private async refresh() {
    const { state, limit, panel } = this.d;
    panel.setStats({
      applied: state.applied,
      daily: await limit.count(),
      dailyLimit: limit.limit,
    });
  }
}
