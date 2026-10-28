import type { AppState } from "../core/state";
import type { Logger } from "../infra/logger";
import { showListModal } from "../ui/modal";

// Списки «Аутлайн» и «Непонятное» в панели. Отчёт по откликам и анкеты — на сервере (веб-интерфейс backend, #46)
export class ReportActions {
  constructor(
    private state: AppState,
    private logger: Logger,
    private persist: () => Promise<void>,
    // Подтверждение пользователя (в панели)
    private ask: (message: string, yesLabel: string) => Promise<boolean>,
  ) {}

  showOutline() {
    showListModal("Аутлайн", this.state.outline);
  }

  showUnclear() {
    showListModal("Непонятное", this.state.unclear);
  }

  async clear(): Promise<void> {
    if (!(await this.ask("Очистить аутлайн и непонятное?", "Очистить")))
      return;

    this.state.clearOutline();
    await this.persist();
    this.logger.log("Аутлайн и непонятное очищены", "success");
  }
}
