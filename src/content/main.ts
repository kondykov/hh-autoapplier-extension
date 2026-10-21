import { loadConfig } from "../core/config";
import { DailyLimit } from "../core/daily-limit";
import { ChromeStorage } from "../infra/storage";

// Composition root: единственное место, где собираются зависимости.
// Остальные модули (apply pipeline, UI, collector...) подключаются здесь по плану docs/ARCHITECTURE.md
async function bootstrap() {
  const storage = new ChromeStorage();
  const config = await loadConfig(storage);
  const limit = new DailyLimit(storage, config.dailyLimit);
  console.log("[HH-AutoApply] ready, remaining today:", await limit.remaining());
}

void bootstrap();
