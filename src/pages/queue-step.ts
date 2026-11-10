import { describeServer, incompatibleMessage, UNKNOWN_VERSION_MESSAGE } from "../api/version";
import type { ApplyResult } from "../apply/pipeline";
import { isAlreadyRespondedOnPage } from "../apply/outcome-detector";
import { pauseBetween, RANGES, wait } from "../core/delays";
import type { AppState, Vacancy } from "../core/state";
import type { PageType } from "../dom/page-type";
import { toServerQuestions } from "../questions/fill";
import { isPageOf, isQueueFailure, pickNext, QUEUE_FAIL_LIMIT, responseUrl, toStatusReport, type QueueItem } from "../queue/queue-mode";
import { extractVacancyInfo } from "./vacancy-info";
import { reportDailyLimit, stopQueue, type PageStep, type StepContext } from "./step-context";

// Режим «из очереди»: следующая одобренная вакансия с backend → страница отклика → исход на сервер → следующая
export class QueueStep implements PageStep {
  readonly name = "очередь";

  constructor(private ctx: StepContext) {}

  // Очередь сама решает, что делать на любой странице: открыть текущую вакансию или перейти к следующей
  accepts(_page: PageType, state: AppState): boolean {
    return state.queueRunning && this.owns(state);
  }

  // Очередь ведёт эта вкладка (#51). Вкладка неизвестна (нет фона) или очередь старая, без вкладки, — как раньше
  owns(state: AppState): boolean {
    const self = this.ctx.tab.self;
    return self === null || state.queueTab === null || state.queueTab === self;
  }

  // Вкладку-владельца закрыли (или браузер перезапущен — id вкладок новые): очередь продолжает эта вкладка
  async adoptOrphan(): Promise<void> {
    const { state, tab, logger } = this.ctx;
    if (!state.queueRunning || this.owns(state) || tab.self === null) return;
    if (await tab.isOpen(state.queueTab!)) return;
    state.queueTab = tab.self;
    await this.ctx.persist();
    logger.log("Вкладки, которая вела очередь, больше нет — очередь продолжает эта вкладка", "warn");
  }

  // Необработанная ошибка шага (#52) — очередь на паузу с сообщением, а не «Стоп очередь» без движения
  toggle(): Promise<void> {
    return this.guarded(() => this.doToggle());
  }

  resume(): Promise<void> {
    return this.guarded(() => this.doResume());
  }

  private async guarded(action: () => Promise<void>): Promise<void> {
    try {
      await action();
    } catch (e) {
      stopQueue(this.ctx, `Ошибка очереди: ${(e as Error).message}`);
      await this.ctx.persist().catch(() => {});
    }
  }

  private async doToggle(): Promise<void> {
    const { state, panel, logger, config, captcha } = this.ctx;
    if (state.queueRunning) {
      stopQueue(this.ctx, this.owns(state) ? "Режим «из очереди» остановлен" : "Очередь в другой вкладке остановлена");
      await this.ctx.persist();
      return;
    }

    state.queueRunning = true;
    state.queueTab = this.ctx.tab.self;
    state.queueRunId = `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
    state.queueCurrent = null;
    state.queueSubmitted = null;
    state.queueTried = [];
    state.queueRunApplied = 0;
    state.queueFailStreak = 0;
    // Старая отметка уже решённой капчи не должна остановить новый запуск; капчу на странице consumeHit увидит и потом
    captcha.consumeHit();
    panel.setQueueActive(true);
    logger.log(
      config.queueDryRun ? "Очередь: пробный режим — «Отправить» не нажимается, статусы на сервер не уходят" : "Очередь: отклики отправляются",
      config.queueDryRun ? "warn" : "step",
    );
    await this.ctx.persist();
    if (await this.serverCompatible()) await this.openNext();
  }

  // Несовместимый backend мог бы принять статусы иначе, чем думает расширение (#77): откликаться с ним нельзя.
  // Сборка без версии — только предупреждение: проверить нечем, а обновляется backend отдельно
  private async serverCompatible(): Promise<boolean> {
    const { state, backend, logger } = this.ctx;
    const version = await backend.getServerVersion();
    if (!state.queueRunning) return false;

    switch (version.state) {
      case "compatible":
        logger.log(`Сервер ${describeServer(version.server)}`, "step");
        return true;
      case "unknown":
        logger.log(UNKNOWN_VERSION_MESSAGE, "warn");
        return true;
      case "incompatible":
        stopQueue(this.ctx, incompatibleMessage(version.server));
        break;
      case "error":
        stopQueue(this.ctx, `Версия сервера недоступна: ${version.error}`);
        break;
    }
    await this.ctx.persist();
    return false;
  }

  private async doResume(): Promise<void> {
    const { state, logger, panel, captcha, pipeline, nav } = this.ctx;
    // Раньше ожидания капчи: решённая капча сама перезагружает страницу, и исход стал бы «не подтверждён» (#78)
    if (state.queueCurrent && state.queueSubmitted === state.queueCurrent.id) {
      await this.resumeSubmitted(state.queueCurrent);
      return;
    }
    await captcha.waitUntilClear();
    // Капча до начала отклика уже дождалась человека — конвейер следит только за новой
    captcha.consumeHit();
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
      // Слетел вход (hh.ru ведёт на регистрацию), блокировка или другой сбой: о вакансии это ничего не говорит, а следующие
      // получили бы то же самое (#75). Статус не трогаем — отклик остаётся в очереди
      stopQueue(this.ctx, `Вместо формы отклика "${current.title}" открылась страница ${new URL(nav.url()).pathname} — очередь остановлена, статус не менялся. Проверьте вход на hh.ru`);
      await this.ctx.persist();
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

    if (await this.archived(current)) {
      // Пока шёл запрос, очередь остановили или перезапустили с другой вакансией — её не трогаем
      if (!state.queueRunning || state.queueCurrent?.id !== current.id) return;
      await this.finish(current, "archived", vacancy.resume);
      return;
    }

    let result: ApplyResult;
    try {
      result = await pipeline.applyOnPage(vacancy);
    } catch (e) {
      stopQueue(this.ctx, `Ошибка: ${(e as Error).message}`);
      await this.ctx.persist();
      return;
    }

    // Остановку уже записал stopQueue; при перезапуске в работе другая вакансия — её не трогаем
    if (result === "stopped") return;
    if (result === "captcha") {
      // Отметка ставится до клика: по ней видно, нажата ли «Отправить»
      stopQueue(this.ctx, state.queueSubmitted === current.id ? captchaAfterSubmit(current) : "Капча посреди отклика: «Отправить» не нажата — решите капчу и запустите очередь снова");
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
        state.queueFailStreak = 0;
        await this.advance();
        return;
      }
    }
    await this.finish(current, result, vacancy.resume);
  }

  // Форма архивной вакансии открывается как обычно (проверено 2026-11-10): признак — только на странице вакансии.
  // Архив закрывает только явный признак; ошибка проверки — отклик идёт как обычно
  private async archived(item: QueueItem): Promise<boolean> {
    const { logger, config } = this.ctx;
    const check = await this.ctx.archive.check(item.id);
    if (check.state === "unknown") {
      logger.log(`Проверка архива "${item.title}" не удалась (${check.error}) — отклик как обычно`, "warn");
      return false;
    }
    if (check.state === "open") return false;
    logger.log(`"${item.title}": вакансия в архиве — «Отправить» не нажимается${config.queueDryRun ? ", пробный режим: статус не отправляется" : ""}`, "warn");
    return true;
  }

  // «Отправить» уже нажата, и hh.ru загрузил новую страницу раньше, чем пришёл исход (#52): отклик, скорее всего, ушёл
  private async resumeSubmitted(current: QueueItem): Promise<void> {
    const result = await waitSubmitted(() => this.ctx.captcha.consumeHit());
    if (result === "captcha") {
      stopQueue(this.ctx, captchaAfterSubmit(current));
      await this.ctx.persist();
      return;
    }
    this.ctx.logger.log(`После «Отправить» загрузилась новая страница: "${current.title}" — ${result === "success" ? "отклик подтверждён" : "не подтверждён, проверить вручную"}`, "warn");
    await this.finish(current, result);
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
    if (await this.stoppedByCaptcha()) return;
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
    // Пока ждали, режим могли остановить кнопкой или могла появиться капча
    if (!state.queueRunning || (await this.stoppedByCaptcha())) return;
    state.queueOpened = true;
    await this.ctx.persist();
    nav.go(responseUrl(next.id));
  }

  // Переход увёл бы со страницы капчи, а решить её должен человек (#78): следующая вакансия не открывается, статусы не меняются
  private async stoppedByCaptcha(): Promise<boolean> {
    if (!this.ctx.captcha.consumeHit()) return false;
    stopQueue(this.ctx, "Капча: очередь остановлена, следующая вакансия не открыта — решите капчу и запустите очередь снова");
    await this.ctx.persist();
    return true;
  }

  // Сообщить исход на сервер (кроме пробного режима) и перейти к следующей вакансии.
  // Отклик, ушедший на hh.ru, сообщается всегда: очередь могли остановить или включить пробный режим, пока ждали подтверждения
  private async finish(item: QueueItem, result: ApplyResult, resume?: string): Promise<void> {
    const { state, logger, backend, config } = this.ctx;
    // Лимит запуска учтён в момент нажатия (onSubmit)
    const sentToHh = result === "success" || result === "unclear";
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
    // Неудачи подряд — скорее сбой на стороне hh.ru (страница блокировки на адресе формы), чем плохие вакансии (#75)
    state.queueFailStreak = isQueueFailure(result) ? state.queueFailStreak + 1 : 0;
    if (state.queueRunning && state.queueFailStreak >= QUEUE_FAIL_LIMIT) {
      stopQueue(this.ctx, `Очередь остановлена: ${state.queueFailStreak} неудачи подряд — проверьте вход на hh.ru и страницу вакансии`);
      await this.ctx.persist();
      return;
    }
    await this.advance();
  }

  private async advance(): Promise<void> {
    const { state } = this.ctx;
    state.queueCurrent = null;
    state.queueSubmitted = null;
    await this.ctx.persist();
    if (state.queueRunning)
      await this.openNext();
  }
}

const captchaAfterSubmit = (item: QueueItem) =>
  `Капча после нажатия «Отправить»: отклик на "${item.title}" мог уйти, статус не менялся — решите капчу, проверьте отклик на hh.ru и запустите очередь снова`;

// Исход на только что загруженной странице: hh.ru дорисовывает её не сразу
async function waitSubmitted(captchaHit: () => boolean, timeoutMs = 3000): Promise<"success" | "unclear" | "captcha"> {
  const start = Date.now();
  do {
    if (captchaHit()) return "captcha";
    if (isAlreadyRespondedOnPage()) return "success";
    if (Date.now() - start >= timeoutMs) return "unclear";
    await wait(300);
  } while (true);
}
