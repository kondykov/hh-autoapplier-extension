import { BackendClient, runtimeSender } from "../api/client";
import { loadConfig } from "../core/config";
import { Controller } from "../core/controller";
import { DailyLimit } from "../core/daily-limit";
import { StateRepository } from "../core/state";
import { ConsoleSink, Logger } from "../infra/logger";
import { ChromeStorage } from "../infra/storage";
import { StaticTemplateProvider } from "../letters/letter-provider";
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
  // Письмо профиля резюме приоритетнее универсального
  const letters = new StaticTemplateProvider(
    (v) => config.resumes.find((r) => r.name === v?.resume)?.letter?.trim() || config.coverLetterTemplate,
  );
  const resumes = new ResumeSelector(() => config.resumes, logger);
  const questions = new QuestionStore(storage);
  const applications = new ApplicationStore(storage);

  let controller!: Controller;
  const panel = new Panel(
    storage,
    {
      onCollect: () => void controller.collect(),
      onNext: () => void controller.next(),
      onAuto: () => void controller.toggleAuto(),
      onShowOutline: () => controller.showOutline(),
      onShowUnclear: () => controller.showUnclear(),
      onClear: () => void controller.clear(),
      onDownloadQuestions: () => void controller.downloadQuestions(),
      onDownloadReport: () => void controller.downloadReport(),
      onRejectNext: () => void controller.rejectNext(),
      onAutoReject: () => void controller.toggleAutoReject(),
      onUrlChange: (url) => void controller.setUrl(url),
      onLimitChange: (value) => void controller.setLimit(value),
      onResumesChange: (profiles) => void controller.setResumes(profiles),
      onRulesChange: (rules) => void controller.setRules(rules),
      onLetterChange: (text) => void controller.setLetter(text),
      onPresetSave: (name) => void controller.savePreset(name),
      onPresetDelete: (name) => void controller.deletePreset(name),
      onPresetApply: (name) => void controller.applyPreset(name),
      onServerChange: (url, apiKey) => void controller.setServer(url, apiKey),
      onServerCheck: () => void controller.checkServer(),
      onQueue: () => void controller.toggleQueue(),
      onQueueDryRunChange: (enabled) => void controller.setQueueDryRun(enabled),
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
