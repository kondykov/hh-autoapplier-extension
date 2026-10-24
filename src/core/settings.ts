import type { BackendClient } from "../api/client";
import type { FilterRules } from "../filter/rules";
import type { KeyValueStorage } from "../infra/storage";
import type { Logger } from "../infra/logger";
import type { ResumeProfile } from "../resume/profiles";
import type { Panel } from "../ui/panel";
import { saveConfig, type AppConfig } from "./config";
import type { DailyLimit } from "./daily-limit";
import { detectPageType } from "../dom/page-type";

// Настройки из панели: сохраняются в chrome.storage сразу при изменении
export class Settings {
  constructor(
    private config: AppConfig,
    private storage: KeyValueStorage,
    private panel: Panel,
    private logger: Logger,
    private limit: DailyLimit,
    private backend: BackendClient,
    private refresh: () => Promise<void>,
  ) {}

  private save = () => saveConfig(this.storage, this.config);

  async setUrl(url: string) {
    this.config.searchRedirectUrl = url;
    await this.save();
  }

  async setLimit(limit: number) {
    this.config.dailyLimit = limit;
    this.limit.limit = limit;
    await this.save();
    await this.refresh();
  }

  async setLetter(text: string) {
    this.config.coverLetterTemplate = text;
    await this.save();
  }

  async setResumes(resumes: ResumeProfile[]) {
    this.config.resumes = resumes;
    await this.save();
  }

  async setRules(rules: FilterRules) {
    this.config.rules = rules;
    await this.save();
  }

  // apiKey: null — оставить сохранённый ключ
  async setServer(url: string, apiKey: string | null) {
    this.config.server = { url, apiKey: apiKey ?? this.config.server.apiKey };
    await this.save();
  }

  async checkServer() {
    this.panel.setServerStatus("Проверяю...", true);
    const result = await this.backend.check();
    this.panel.setServerStatus(result.message, result.ok);
    this.logger.log(result.message, result.ok ? "success" : "warn");
  }

  async setQueueDryRun(enabled: boolean) {
    this.config.queueDryRun = enabled;
    await this.save();
    this.logger.log(enabled ? "Пробный режим очереди включён" : "Пробный режим очереди выключен: отклики будут отправляться", enabled ? "info" : "warn");
  }

  async savePreset(name: string) {
    const { config } = this;
    config.presets = [
      ...config.presets.filter((p) => p.name !== name),
      { name, url: config.searchRedirectUrl, letter: config.coverLetterTemplate, rules: config.rules },
    ];
    await this.save();
    this.panel.setPresets(config.presets.map((p) => p.name));
  }

  async deletePreset(name: string) {
    this.config.presets = this.config.presets.filter((p) => p.name !== name);
    await this.save();
    this.panel.setPresets(this.config.presets.map((p) => p.name));
  }

  async applyPreset(name: string) {
    const preset = this.config.presets.find((p) => p.name === name);
    if (!preset)
      return;

    await this.setUrl(preset.url);
    if (preset.rules) {
      await this.setRules(preset.rules);
      this.panel.setRules(preset.rules);
    }
    if (preset.letter) {
      await this.setLetter(preset.letter);
      this.panel.setLetter(preset.letter);
    }
    this.panel.setUrl(preset.url);
    if (detectPageType() === "search" || confirm(`Перейти на поиск "${name}"?`))
      location.href = preset.url;
  }
}
