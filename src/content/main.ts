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
import { Panel } from "../ui/panel";

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
  // Письмо от backend под вакансию приоритетнее письма профиля резюме, а оно — универсального
  const letters = new PreparedLetterProvider(new StaticTemplateProvider(
    (v) => config.resumes.find((r) => r.name === v?.resume)?.letter?.trim() || config.coverLetterTemplate,
  ));
  const resumes = new ResumeSelector(() => config.resumes, logger);
  const questions = new QuestionStore(storage);
  const applications = new ApplicationStore(storage);

  let controller!: Controller;
  const panel = new Panel(
    storage,
    {
      onCollect: () => void controller.search.collect(),
      onNext: () => void controller.search.next(),
      onAuto: () => void controller.search.toggleAuto(),
      onShowOutline: () => controller.reports.showOutline(),
      onShowUnclear: () => controller.reports.showUnclear(),
      onClear: () => void controller.reports.clear(),
      onDownloadQuestions: () => void controller.reports.downloadQuestions(),
      onDownloadReport: () => void controller.reports.downloadReport(),
      onRejectNext: () => void controller.rejects.next(),
      onAutoReject: () => void controller.rejects.toggleAuto(),
      onUrlChange: (url) => void controller.settings.setUrl(url),
      onLimitChange: (value) => void controller.settings.setLimit(value),
      onResumesChange: (profiles) => void controller.settings.setResumes(profiles),
      onRulesChange: (rules) => void controller.settings.setRules(rules),
      onLetterChange: (text) => void controller.settings.setLetter(text),
      onPresetSave: (name) => void controller.settings.savePreset(name),
      onPresetDelete: (name) => void controller.settings.deletePreset(name),
      onPresetApply: (name) => void controller.settings.applyPreset(name),
      onServerChange: (url, apiKey) => void controller.settings.setServer(url, apiKey),
      onServerCheck: () => void controller.settings.checkServer(),
      onQueue: () => void controller.queue.toggle(),
      onQueueDryRunChange: (enabled) => void controller.settings.setQueueDryRun(enabled),
    },
    {
      url: config.searchRedirectUrl,
      limit: config.dailyLimit,
      letter: config.coverLetterTemplate,
      presets: config.presets.map((p) => p.name),
      rules: config.rules,
      resumes: config.resumes,
      server: config.server,
      queueDryRun: config.queueDryRun,
    },
  );
  logger.addSink(panel);
  await panel.mount();

  const backend = new BackendClient(runtimeSender);
  controller = new Controller({ storage, config, state, repo, limit, logger, panel, letters, questions, applications, resumes, backend });
  await controller.init();
  logger.log("Панель загружена");
}

void bootstrap();
