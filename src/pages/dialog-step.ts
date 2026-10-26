import type { AutoReply } from "../api/client";
import { DELAYS, pause } from "../core/delays";
import type { AppState } from "../core/state";
import { ChatSource, describeWriteReason, pageFetch } from "../dialogs/chats";
import { chatUrl, sendChatMessage, type SendResult } from "../dialogs/reply";
import type { PageType } from "../dom/page-type";
import { pageOf, type PageStep, type StepContext } from "./step-context";

export type ChatReader = Pick<ChatSource, "recent" | "writePossibility">;

// Кнопка «Автоответ» на странице чатов (#26): чаты откликов → на сервер (статусы, разбор LLM, Telegram);
// на новые отказы — короткое «спасибо», где hh.ru разрешает писать (решение владельца 2026-10-26).
// Обычно после отказа переписка закрыта до приглашения — тогда сервер узнаёт, что автоответ невозможен.
// Пробный режим (галочка очереди) не отправляет ничего работодателю
export class DialogStep implements PageStep {
  readonly name = "автоответ";

  private running = false;

  constructor(
    private ctx: StepContext,
    private chats: ChatReader = new ChatSource(pageFetch, () => pause(DELAYS.click)),
    private send: (text: string) => Promise<SendResult> = sendChatMessage,
  ) {}

  // Отправка идёт по одному чату на загрузку страницы: каждый чат — отдельный переход
  accepts(page: PageType, state: AppState): boolean {
    return page === "chat" && state.autoReplies.length > 0;
  }

  async run(): Promise<void> {
    const { logger, panel, backend, config, state } = this.ctx;
    if (this.running) return;
    if (pageOf(this.ctx) !== "chat") {
      logger.log("Откройте страницу чатов или откликов hh.ru", "error");
      return;
    }

    this.running = true;
    try {
      panel.setStep("Собираю чаты…");
      let chats;
      try {
        chats = await this.chats.recent();
      } catch (e) {
        logger.log(`Чаты hh.ru не получены: ${(e as Error).message}`, "error");
        return;
      }

      const res = await backend.importDialogs(chats);
      if (!res.ok) {
        logger.log(`Чаты не отправлены на сервер: ${res.error}`, "error");
        return;
      }
      const r = res.result;
      logger.log(`Чатов: ${r.received}, новых сообщений работодателей: ${r.newMessages}, отказов: ${r.rejected}, приглашений: ${r.invited}. ` +
        "Разбор и черновики ответов — в Telegram и на /dialogs.html", "success");

      const writable: AutoReply[] = [];
      for (const reply of r.autoReplies) {
        const label = reply.title || `чат ${reply.chatId}`;
        let possibility;
        try {
          possibility = await this.chats.writePossibility(reply.chatId);
        } catch (e) {
          logger.log(`«${label}»: не узнать, можно ли писать (${(e as Error).message}) — попробую в следующий раз`, "warn");
          continue;
        }
        if (!possibility.canWrite) {
          const reason = describeWriteReason(possibility.reason);
          await backend.reportAutoReply(reply.chatId, false, reason);
          logger.log(`«${label}»: автоответ невозможен — ${reason}`);
          continue;
        }
        if (config.queueDryRun) {
          logger.log(`Пробный режим: «${label}» — ответ на отказ не отправлен`, "warn");
          continue;
        }
        writable.push(reply);
      }

      if (writable.length === 0) {
        panel.setStep("Автоответ: готово");
        return;
      }
      state.autoReplies = writable;
      await this.ctx.persist();
      logger.log(`Отвечаю на отказы: ${writable.length}`, "step");
      this.ctx.nav.go(chatUrl(writable[0].chatId, new URL(this.ctx.nav.url()).origin));
    } finally {
      this.running = false;
    }
  }

  // Открыт чат первого автоответа: отправить, сообщить итог, перейти к следующему
  async resume(): Promise<void> {
    const { state, logger, panel, backend, config, nav } = this.ctx;
    const current = state.autoReplies[0];
    if (!current) return;
    // Пробный режим включили, пока шли переходы, — не отправлять
    if (config.queueDryRun) {
      state.autoReplies = [];
      await this.ctx.persist();
      logger.log("Пробный режим: автоответы остановлены", "warn");
      return;
    }

    const label = current.title || `чат ${current.chatId}`;
    // Не тот чат (hh.ru перенаправил) — не писать в чужой
    const result: SendResult = nav.url().includes(`/chat/${current.chatId}`)
      ? await this.send(config.rejectionReply)
      : { ok: false, error: "чат не открылся" };
    await backend.reportAutoReply(current.chatId, result.ok, result.ok ? undefined : result.error);
    logger.log(result.ok ? `«${label}»: ответ на отказ отправлен` : `«${label}»: ответ не отправлен — ${result.error}`, result.ok ? "success" : "warn");

    state.autoReplies = state.autoReplies.slice(1);
    await this.ctx.persist();
    const next = state.autoReplies[0];
    if (!next) {
      panel.setStep("Автоответ: готово");
      return;
    }
    await pause(DELAYS.step);
    nav.go(chatUrl(next.chatId, new URL(nav.url()).origin));
  }
}
