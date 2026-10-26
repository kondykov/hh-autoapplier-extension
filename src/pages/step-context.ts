import type { BackendClient } from "../api/client";
import type { ApplyPipeline } from "../apply/pipeline";
import type { CaptchaGuard } from "../captcha/guard";
import type { AppConfig } from "../core/config";
import type { DailyLimit } from "../core/daily-limit";
import type { AppState } from "../core/state";
import { detectPageType, type PageType } from "../dom/page-type";
import type { KeyValueStorage } from "../infra/storage";
import type { Logger } from "../infra/logger";
import type { Panel } from "../ui/panel";

// Переходы между страницами hh.ru; в тестах подменяется, чтобы шаги не трогали window.location
export interface Navigator {
  url(): string;
  go(url: string): void;
  reload(): void;
}

export const browserNavigator: Navigator = {
  url: () => location.href,
  go: (url) => {
    location.href = url;
  },
  reload: () => location.reload(),
};

// То, что шагам нужно от панели; полная панель — в ui/panel.ts
export type StepPanel = Pick<Panel, "setStep" | "setQueueActive" | "setRejectActive">;

// Общее для всех шагов: состояние, настройки, панель, конвейер отклика и служебные действия
export interface StepContext {
  state: AppState;
  config: AppConfig;
  storage: KeyValueStorage;
  panel: StepPanel;
  logger: Logger;
  limit: DailyLimit;
  backend: Pick<BackendClient, "getQueue" | "getResumes" | "reportStatus" | "submitQuestionnaire" | "reportEvent" | "importVacancies" | "importDialogs" | "reportAutoReply">;
  pipeline: Pick<ApplyPipeline, "applyOnPage">;
  captcha: Pick<CaptchaGuard, "waitUntilClear" | "consumeHit">;
  nav: Navigator;
  // Сохранить состояние и обновить счётчики панели
  persist(): Promise<void>;
}

export const pageOf = (ctx: StepContext): PageType => detectPageType(ctx.nav.url());

// Дневной лимит — владельцу в Telegram (#28). Ответ не ждём: сервер шлёт одно сообщение в сутки, без сервера запрос просто не дойдёт
export function reportDailyLimit(ctx: StepContext): void {
  void ctx.backend.reportEvent("DailyLimit", `Лимит: ${ctx.limit.limit} откликов в день`);
}

export function stopQueue(ctx: StepContext, message: string): void {
  ctx.state.queueRunning = false;
  ctx.state.queueCurrent = null;
  ctx.panel.setQueueActive(false);
  ctx.panel.setStep(message);
  ctx.logger.log(message, "warn");
}

// Шаг страницы: продолжает работу после загрузки страницы, если его режим включён
export interface PageStep {
  readonly name: string;
  // Шаг берётся за эту страницу в текущем состоянии
  accepts(page: PageType, state: AppState): boolean;
  resume(): Promise<void>;
}

// Первый подходящий шаг
export const pickStep = (steps: PageStep[], page: PageType, state: AppState): PageStep | null =>
  steps.find((s) => s.accepts(page, state)) ?? null;
