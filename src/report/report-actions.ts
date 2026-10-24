import type { AppState, Vacancy } from "../core/state";
import type { Logger } from "../infra/logger";
import { extractQuestions } from "../questions/extract";
import { downloadJson, type QuestionStore } from "../questions/store";
import { showListModal } from "../ui/modal";
import { downloadCsv, toCsv, type ApplicationStore } from "./applications";

// Собранное расширением: анкеты, отчёт по откликам, списки «Аутлайн» и «Непонятное»
export class ReportActions {
  constructor(
    private state: AppState,
    private questions: QuestionStore,
    private applications: ApplicationStore,
    private logger: Logger,
    private persist: () => Promise<void>,
  ) {}

  // Вопросы анкеты с текущей страницы — для будущего автозаполнения
  async saveQuestions(vacancy: Vacancy): Promise<void> {
    const answers = extractQuestions();
    if (!answers.length)
      return;

    const added = await this.questions.add({
      vacancyTitle: vacancy.title,
      vacancyUrl: vacancy.link || location.href,
      collectedAt: new Date().toISOString(),
      answers,
    });
    if (added)
      this.logger.log(`Сохранено вопросов: ${answers.length}`, "success");
  }

  async downloadQuestions(): Promise<void> {
    const all = await this.questions.list();
    if (!all.length) {
      this.logger.log("Нет собранных вопросов", "warn");
      return;
    }
    downloadJson(`collected_questions_${new Date().toISOString().slice(0, 10)}.json`, all);
    this.logger.log(`Скачано анкет: ${all.length}`, "success");
  }

  async downloadReport(): Promise<void> {
    const all = await this.applications.list();
    if (!all.length) {
      this.logger.log("Откликов для отчёта пока нет", "warn");
      return;
    }
    downloadCsv(`applications_${new Date().toISOString().slice(0, 10)}.csv`, toCsv(all));
    this.logger.log(`Отчёт: ${all.length} откликов`, "success");
  }

  showOutline() {
    showListModal("Аутлайн", this.state.outline);
  }

  showUnclear() {
    showListModal("Непонятное", this.state.unclear);
  }

  async clear(): Promise<void> {
    if (!confirm("Очистить аутлайн, непонятное и собранные вопросы?"))
      return;

    this.state.clearOutline();
    await this.questions.clear();
    await this.persist();
    this.logger.log("Аутлайн, пропуски и вопросы очищены", "success");
  }
}
