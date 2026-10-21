import { loadConfig } from "../core/config";
import { Controller } from "../core/controller";
import { DailyLimit } from "../core/daily-limit";
import { StateRepository } from "../core/state";
import { ConsoleSink, Logger } from "../infra/logger";
import { ChromeStorage } from "../infra/storage";
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

  let controller!: Controller;
  const panel = new Panel(
    storage,
    {
      onCollect: () => void controller.collect(),
      onNext: () => void controller.next(),
      onAuto: () => controller.toggleAuto(),
      onShowOutline: () => controller.showOutline(),
      onShowUnclear: () => controller.showUnclear(),
      onClear: () => void controller.clear(),
      onUrlChange: (url) => void controller.setUrl(url),
      onLimitChange: (value) => void controller.setLimit(value),
    },
    { url: config.searchRedirectUrl, limit: config.dailyLimit },
  );
  logger.addSink(panel);
  await panel.mount();

  controller = new Controller({ storage, config, state, repo, limit, logger, panel });
  await controller.init();
  logger.log("Панель загружена");
}

void bootstrap();
