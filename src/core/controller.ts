import type { BackendClient } from "../api/client";
import { ApplyPipeline } from "../apply/pipeline";
import { CaptchaGuard } from "../captcha/guard";
import type { KeyValueStorage } from "../infra/storage";
import type { TabInfo } from "../infra/tab";
import type { Logger } from "../infra/logger";
import type { LetterProvider } from "../letters/letter-provider";
import { DialogStep } from "../pages/dialog-step";
import { QueueStep } from "../pages/queue-step";
import { RejectStep } from "../pages/reject-step";
import { SearchStep } from "../pages/search-step";
import { browserNavigator, pageOf, pickStep, stopQueue, type Navigator, type StepContext } from "../pages/step-context";
import { isDryRun, isQueueOn } from "../queue/queue-mode";
import type { ResumeSelector } from "../resume/selector";
import type { Panel } from "../ui/panel";
import { watchConfig, type AppConfig } from "./config";
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
  tab?: TabInfo;
}

// Без service worker (тесты, старый фон): очередь не привязана к вкладке
const ANY_TAB: TabInfo = { self: null, isOpen: async () => true };

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
      d.logger.log(shown ? "Капча: работа на паузе" : d.state.queueRunning ? "Капча решена, продолжаю" : "Капча решена", shown ? "warn" : "success");
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
      tab: d.tab ?? ANY_TAB,
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
    this.d.repo.watch((saved) => this.onExternalState(saved));
    watchConfig(this.d.storage, (config) => this.onExternalConfig(config));
    await this.queue.adoptOrphan();
    panel.setQueueActive(state.queueRunning);
    if (state.queueRunning && !this.queue.owns(state)) {
      panel.setStep("Очередь идёт в другой вкладке — «Стоп очередь» остановит её");
      return;
    }

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
      logger: d.logger,
      letters: d.letters,
      resumes: d.resumes,
      // Лимиты уже учтены в onSubmit — в момент нажатия
      onApplied: async (vacancy) => {
        d.state.applied++;
        d.logger.log(`Отклик зарегистрирован: "${vacancy.title}"`, "success");
      },
      // Отметка и лимиты — до клика (#52): после него hh.ru может сразу загрузить новую страницу, а «Непонятное» тоже ушло
      onSubmit: async () => {
        d.state.queueSubmitted = d.state.queueCurrent?.id ?? null;
        d.state.queueRunApplied++;
        await d.limit.increment();
        await this.persist();
      },
      captchaHit: () => this.captcha.consumeHit(),
      onChange: () => this.persist(),
      dryRun: () => isDryRun(d.state.queueRunning, d.config.queueDryRun),
      active: (vacancy) => isQueueOn(d.state.queueRunning, d.state.queueCurrent, vacancy.link),
    });
  }

  // Состояние записала другая вкладка (#51)
  private onExternalState(saved: AppState): void {
    const { state, panel } = this.d;
    if (state.queueRunning && this.queue.owns(state)) {
      // Своя очередь: из другой вкладки принимается только остановка этого же запуска
      if (!saved.queueRunning && saved.queueRunId === state.queueRunId)
        stopQueue(this.ctx, "Очередь остановлена в другой вкладке");
      return;
    }
    // Свой остановленный запуск не воскрешать: другая вкладка могла записать его до того, как узнала об остановке
    if (saved.queueRunning && saved.queueRunId === state.queueRunId && state.queueTab === this.ctx.tab.self) return;
    // Чужая очередь (или её нет): поля очереди — как в хранилище, чтобы своя запись их не затёрла
    state.queueRunning = saved.queueRunning;
    state.queueCurrent = saved.queueCurrent;
    state.queueTried = saved.queueTried;
    state.queueRunApplied = saved.queueRunApplied;
    state.queueFailStreak = saved.queueFailStreak;
    state.queueOpened = saved.queueOpened;
    state.queueSubmitted = saved.queueSubmitted;
    state.queueTab = saved.queueTab;
    state.queueRunId = saved.queueRunId;
    panel.setQueueActive(state.queueRunning);
    if (state.queueRunning) panel.setStep("Очередь идёт в другой вкладке — «Стоп очередь» остановит её");
  }

  // Настройки сохранила другая вкладка (#51): пробный режим и лимиты — сразу, в том числе для вакансии в работе
  private onExternalConfig(config: AppConfig): void {
    const { logger, panel, limit } = this.d;
    const dryRunChanged = config.queueDryRun !== this.d.config.queueDryRun;
    Object.assign(this.d.config, config);
    limit.limit = config.dailyLimit;
    panel.setSettings(config);
    if (dryRunChanged)
      logger.log(config.queueDryRun ? "Пробный режим включён в другой вкладке" : "Пробный режим выключен в другой вкладке", "warn");
    void this.refresh();
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
