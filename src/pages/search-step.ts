import { collectCards } from "../search/vacancy-collector";
import { pageOf, type StepContext } from "./step-context";

// Выдача hh.ru → backend (#27): запасной сбор, когда серверный сбор упёрся в антибот. Карточки текущей страницы
// уходят в поиск на сервере; отбор, оценка, резюме и письмо — там, отклик — через режим «из очереди»
export class SearchStep {
  private busy = false;

  constructor(private ctx: StepContext) {}

  async send(): Promise<boolean> {
    if (this.busy)
      return false;

    const { logger, panel, backend, config } = this.ctx;
    if (pageOf(this.ctx) !== "search") {
      logger.log("Откройте страницу поиска вакансий (/search/vacancy)", "error");
      return false;
    }
    const search = config.importSearch.trim();
    if (!search) {
      logger.log("Укажите в настройках сервера поиск, в который отправлять выдачу", "error");
      panel.setStep("Не задан поиск на сервере");
      return false;
    }

    const cards = collectCards();
    if (!cards.length) {
      logger.log("На странице нет вакансий без отклика", "warn");
      panel.setStep("Нечего отправлять");
      return false;
    }

    this.busy = true;
    panel.setStep(`Отправляю на сервер: ${cards.length}`);
    try {
      const sent = await backend.importVacancies(search, cards);
      if (!sent.ok) {
        logger.log(`Выдача не дошла до сервера: ${sent.error}`, "error");
        panel.setStep("Ошибка отправки");
        return false;
      }

      const { received, added, queued, skipped, waiting } = sent.result;
      const message = `Сервер: получено ${received}, новых ${added} (в очередь или на оценку ${queued}, отсеяно ${skipped}`
        + (waiting ? `, ждут подтверждения ${waiting}` : "") + ")";
      logger.log(message, "success");
      panel.setStep(message);
      return true;
    } finally {
      this.busy = false;
    }
  }
}
