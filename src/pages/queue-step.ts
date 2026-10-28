import type { ApplyResult } from "../apply/pipeline";
import { pauseBetween, RANGES } from "../core/delays";
import type { AppState, Vacancy } from "../core/state";
import type { PageType } from "../dom/page-type";
import { toServerQuestions } from "../questions/fill";
import { isPageOf, pickNext, responseUrl, toStatusReport, type QueueItem } from "../queue/queue-mode";
import { extractVacancyInfo } from "./vacancy-info";
import { reportDailyLimit, stopQueue, type PageStep, type StepContext } from "./step-context";

// Режим «из очереди»: следующая одобренная вакансия с backend → страница отклика → исход на сервер → следующая
export class QueueStep implements PageStep {
  readonly name = "очередь";

  constructor(private ctx: StepContext) {}

  // Очередь сама решает, что делать на любой странице: открыть текущую вакансию или перейти к следующей
  accepts(_page: PageType, state: AppState): boolean {
    return state.queueRunning;
  }

  async toggle(): Promise<void> {
    const { state, panel, logger, config } = this.ctx;
    if (state.queueRunning) {
      stopQueue(this.ctx, "Режим «из очереди» остановлен");
      await this.ctx.persist();
      return;
    }

    state.queueRunning = true;
    state.queueCurrent = null;
    state.queueTried = [];
    state.queueRunApplied = 0;
    panel.setQueueActive(true);
    logger.log(
      config.queueDryRun ? "Очередь: пробный режим — «Отправить» не нажимается, статусы на сервер не уходят" : "Очередь: отклики отправляются",
      config.queueDryRun ? "warn" : "step",
    );
    await this.ctx.persist();
    await this.openNext();
  }

  async resume(): Promise<void> {
    const { state, logger, panel, captcha, pipeline, nav } = this.ctx;
    await captcha.waitUntilClear();
    const current = state.queueCurrent;
    if (!current) {
      await this.openNext();
      return;
    }
    if (!isPageOf(nav.url(), current.id) && !state.queueOpened) {
      // Страница загрузилась раньше перехода к текущей вакансии (её открыл hh.ru после прошлого отклика) — открыть форму
      state.queueOpened = true;
      await this.ctx.persist();
      nav.go(responseUrl(current.id));
      return;
    }
    if (!isPageOf(nav.url(), current.id)) {
      // hh.ru не открыл форму отклика (вакансия в архиве, перенаправил на другую страницу) — идём дальше
      logger.log(`Форма отклика для "${current.title}" не открылась`, "warn");
      await this.finish(current, "skipped");
      return;
    }

    const info = extractVacancyInfo();
    const vacancy: Vacancy = {
      ...info,
      title: current.title || info.title,
      company: current.company || info.company,
      link: nav.url(),
      resume: current.resume ?? undefined,
      resumeMatch: current.resumeMatch,
      letter: current.letter,
      answers: current.answers,
    };
    panel.setStep(`Очередь: отклик на "${vacancy.title}"`);

    let result: ApplyResult;
    try {
      result = await pipeline.applyOnPage(vacancy);
    } catch (e) {
      stopQueue(this.ctx, `Ошибка: ${(e as Error).message}`);
      await this.ctx.persist();
      return;
    }

    if (result === "disabled") {
      stopQueue(this.ctx, 'Кнопка "Отправить" неактивна — проверьте форму вручную');
      await this.ctx.persist();
      return;
    }
    if (result === "dry_run")
      logger.log(`Пробный режим: "${vacancy.title}" готов к отправке`, "success");
    if (result === "outline") {
      const sent = await this.sendQuestionnaire(current);
      if (sent === "stopped") return;
      if (sent === "sent") {
        await this.advance();
        return;
      }
    }
    await this.finish(current, result, vacancy.resume);
  }

  // Анкета без ответов (или поменялась): вопросы на сервер — LLM ответит, вакансия вернётся в очередь.
  // Уходит и в пробном режиме: это не отклик работодателю, а подготовка к нему
  private async sendQuestionnaire(item: QueueItem): Promise<"sent" | "none" | "stopped"> {
    const { logger, backend } = this.ctx;
    const questions = toServerQuestions();
    if (questions.length === 0) return "none";

    const sent = await backend.submitQuestionnaire(item.id, questions);
    if (!sent.ok) {
      stopQueue(this.ctx, `Анкета "${item.title}" не дошла до сервера: ${sent.error}`);
      await this.ctx.persist();
      return "stopped";
    }
    logger.log(`Анкета "${item.title}": ${questions.length} вопр. на сервере, вакансия вернётся в очередь с ответами LLM`, "warn");
    return "sent";
  }

  private async openNext(): Promise<void> {
    const { state, panel, logger, backend, limit, nav, config } = this.ctx;
    if (config.queueRunLimit > 0 && state.queueRunApplied >= config.queueRunLimit) {
      stopQueue(this.ctx, `Лимит запуска: отправлено ${state.queueRunApplied} из ${config.queueRunLimit}`);
      await this.ctx.persist();
      return;
    }
    if (await limit.isReached()) {
      stopQueue(this.ctx, `Достигнут дневной лимит (${limit.limit})`);
      reportDailyLimit(this.ctx);
      await this.ctx.persist();
      return;
    }

    const queue = await backend.getQueue();
    if (!queue.ok) {
      stopQueue(this.ctx, `Очередь недоступна: ${queue.error}`);
      await this.ctx.persist();
      return;
    }

    const next = pickNext(queue.items, state.queueTried);
    if (!next) {
      stopQueue(this.ctx, "Очередь пуста: одобренных вакансий больше нет");
      await this.ctx.persist();
      return;
    }

    // Резюме выбирает backend; на hh.ru оно находится по match профиля — тоже с сервера
    if (next.resume) {
      const resumes = await backend.getResumes();
      if (!resumes.ok) {
        stopQueue(this.ctx, `Резюме с сервера недоступны: ${resumes.error}`);
        await this.ctx.persist();
        return;
      }
      next.resumeMatch = resumes.resumes.find((r) => r.name === next.resume)?.match;
      if (!next.resumeMatch)
        logger.log(`Резюме «${next.resume}» нет на сервере — оставлю выбранное hh.ru`, "warn");
    }

    state.queueCurrent = next;
    state.queueOpened = false;
    state.queueTried.push(next.id);
    await this.ctx.persist();
    panel.setStep(`Очередь: "${next.title}"`);
    logger.log(`Очередь: открываю "${next.title}" (${next.company})`, "step");
    await pauseBetween(RANGES.nextVacancy);
    // Пока ждали, режим могли остановить кнопкой
    if (state.queueRunning) {
      state.queueOpened = true;
      await this.ctx.persist();
      nav.go(responseUrl(next.id));
    }
  }

  // Сообщить исход на сервер (кроме пробного режима) и перейти к следующей вакансии.
  // Отклик, ушедший на hh.ru, сообщается всегда: очередь могли остановить или включить пробный режим, пока ждали подтверждения
  private async finish(item: QueueItem, result: ApplyResult, resume?: string): Promise<void> {
    const { state, logger, backend, config } = this.ctx;
    const sentToHh = result === "success" || result === "unclear";
    if (sentToHh) state.queueRunApplied++;
    const report = sentToHh || (state.queueRunning && !config.queueDryRun) ? toStatusReport(result) : null;
    if (report) {
      const sent = await backend.reportStatus(item.id, report, resume ?? item.resume);
      if (!sent.ok) {
        stopQueue(this.ctx, `Статус "${item.title}" не дошёл до сервера: ${sent.error}`);
        await this.ctx.persist();
        return;
      }
      logger.log(`Сервер: "${item.title}" → ${report.status}`, report.status === "Applied" ? "success" : "warn");
    }
    await this.advance();
  }

  private async advance(): Promise<void> {
    const { state } = this.ctx;
    state.queueCurrent = null;
    await this.ctx.persist();
    if (state.queueRunning)
      await this.openNext();
  }
}
