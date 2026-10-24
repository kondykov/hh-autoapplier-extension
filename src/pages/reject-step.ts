import { openNextRejection } from "../rejects/runner";
import type { StepContext } from "./step-context";

// Чаты с работодателями: открыть следующий отказ, по кнопке — подряд, пока отказы не кончатся
export class RejectStep {
  private running = false;

  constructor(private ctx: StepContext) {}

  async next(): Promise<boolean> {
    const { logger, panel, nav } = this.ctx;
    const url = nav.url();
    if (!url.includes("/chat") && !url.includes("/applicant/negotiations")) {
      logger.log("Откройте страницу чатов (/chat или /applicant/negotiations)", "error");
      return false;
    }

    const found = await openNextRejection(logger);
    if (!found)
      panel.setStep("Отказов больше нет");
    return found;
  }

  async toggleAuto(): Promise<void> {
    this.running = !this.running;
    this.ctx.panel.setRejectActive(this.running);
    while (this.running) {
      if (!(await this.next()))
        this.running = false;
    }
    this.ctx.panel.setRejectActive(false);
  }
}
