import type { BackendClient } from "../api/client";
import type { KeyValueStorage } from "../infra/storage";
import type { Logger } from "../infra/logger";
import type { Panel } from "../ui/panel";
import { saveConfig, type AppConfig } from "./config";
import type { DailyLimit } from "./daily-limit";

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

  // apiKey: null — оставить сохранённый ключ
  async setServer(url: string, apiKey: string | null) {
    this.config.server = { url, apiKey: apiKey ?? this.config.server.apiKey };
    await this.save();
  }

  async setImportSearch(name: string) {
    this.config.importSearch = name;
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
}
