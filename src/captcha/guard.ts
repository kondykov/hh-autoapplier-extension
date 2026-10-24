import { wait } from "../core/delays";
import { SELECTORS } from "../core/selectors";

export const isCaptchaVisible = (root: ParentNode = document): boolean =>
  root.querySelector(SELECTORS.captcha) !== null;

// Следит за капчей, пока она на странице; блокирует авто-режим до её решения человеком
export class CaptchaGuard {
  private hit = false;
  private timer: ReturnType<typeof setInterval> | undefined;
  private shown = false;

  constructor(private onChange: (shown: boolean) => void, private pollMs = 1000) {}

  start(): void {
    this.timer = setInterval(() => this.tick(), this.pollMs);
    this.tick();
  }

  stop(): void {
    clearInterval(this.timer);
  }

  private tick(): void {
    const now = isCaptchaVisible();
    if (now) this.hit = true;
    if (now !== this.shown) {
      this.shown = now;
      this.onChange(now);
    }
  }

  // true, если капча появлялась с прошлого вызова: текущий шаг мог быть сорван и его надо повторить
  consumeHit(): boolean {
    const was = this.hit || isCaptchaVisible();
    this.hit = false;
    return was;
  }

  async waitUntilClear(): Promise<boolean> {
    if (!isCaptchaVisible()) return false;
    while (isCaptchaVisible()) await wait(this.pollMs);
    await wait(this.pollMs * 2);
    return true;
  }
}
