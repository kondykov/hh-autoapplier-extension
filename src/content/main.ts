import { BackendClient, runtimeSender } from "../api/client";
import { loadConfig } from "../core/config";
import { Controller } from "../core/controller";
import { DailyLimit } from "../core/daily-limit";
import { StateRepository } from "../core/state";
import { ConsoleSink, Logger } from "../infra/logger";
import { ChromeStorage } from "../infra/storage";
import { PreparedLetterProvider, StaticTemplateProvider } from "../letters/letter-provider";
import { QuestionStore } from "../questions/store";
import { ApplicationStore } from "../report/applications";
import { ResumeSelector } from "../resume/selector";
import { Panel, VERSION } from "../ui/panel";

// Composition root: единственное место, где собираются зависимости
async function bootstrap() {
  if (document.getElementById("hh-autoapply-panel")) return;

  const storage = new ChromeStorage();
  const config = await loadConfig(storage);
  const repo = new StateRepository(storage);
  const state = await repo.load();
  const limit = new DailyLimit(storage, config.dailyLimit);
  const logger = new Logger();
  logger.addSink(new ConsoleSink());
  // Письмо от backend под вакансию приоритетнее универсального из панели
  const letters = new PreparedLetterProvider(new StaticTemplateProvider(() => config.coverLetterTemplate));
  const resumes = new ResumeSelector(logger);
  const questions = new QuestionStore(storage);
  const applications = new ApplicationStore(storage);

  let controller!: Controller;
  const panel = new Panel(
    storage,
    {
      onSendSearch: () => void controller.search.send(),
      onShowOutline: () => controller.reports.showOutline(),
      onShowUnclear: () => controller.reports.showUnclear(),
      onClear: () => void controller.reports.clear(),
      onDownloadQuestions: () => void controller.reports.downloadQuestions(),
      onDownloadReport: () => void controller.reports.downloadReport(),
      onRejectNext: () => void controller.rejects.next(),
      onAutoReject: () => void controller.rejects.toggleAuto(),
      onAutoReply: () => void controller.dialogs.run(),
      onRejectionReplyChange: (text) => void controller.settings.setRejectionReply(text),
      onLimitChange: (value) => void controller.settings.setLimit(value),
      onQueueRunLimitChange: (value) => void controller.settings.setQueueRunLimit(value),
      onLetterChange: (text) => void controller.settings.setLetter(text),
      onServerChange: (url, apiKey) => void controller.settings.setServer(url, apiKey),
      onImportSearchChange: (name) => void controller.settings.setImportSearch(name),
      onServerCheck: () => void controller.settings.checkServer(),
      onQueue: () => void controller.queue.toggle(),
      onQueueDryRunChange: (enabled) => void controller.settings.setQueueDryRun(enabled),
    },
    {
      limit: config.dailyLimit,
      queueRunLimit: config.queueRunLimit,
      letter: config.coverLetterTemplate,
      server: config.server,
      importSearch: config.importSearch,
      queueDryRun: config.queueDryRun,
      rejectionReply: config.rejectionReply,
    },
  );
  logger.addSink(panel);
  await panel.mount();

  const backend = new BackendClient(runtimeSender);
  controller = new Controller({ storage, config, state, repo, limit, logger, panel, letters, questions, applications, resumes, backend });
  await controller.init();
  logger.log(`Панель загружена (v${VERSION})`);
}

void bootstrap();
