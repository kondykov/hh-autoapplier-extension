import { describe, expect, it } from "vitest";
import { CaptchaGuard, isCaptchaVisible } from "../src/captcha/guard";

describe("captcha guard", () => {
  it("обнаруживает капчу и ждёт её исчезновения", async () => {
    document.body.innerHTML = '<input data-qa="account-captcha-input" />';
    expect(isCaptchaVisible()).toBe(true);
    const guard = new CaptchaGuard(() => {}, 10);
    setTimeout(() => (document.body.innerHTML = ""), 50);
    expect(await guard.waitUntilClear()).toBe(true);
    expect(isCaptchaVisible()).toBe(false);
    expect(await guard.waitUntilClear()).toBe(false);
  });

  it("consumeHit помнит появление капчи", () => {
    document.body.innerHTML = '<img data-qa="account-captcha-picture" />';
    const changes: boolean[] = [];
    const guard = new CaptchaGuard((s) => changes.push(s), 10);
    guard.start();
    document.body.innerHTML = "";
    expect(guard.consumeHit()).toBe(true);
    expect(guard.consumeHit()).toBe(false);
    guard.stop();
    expect(changes).toEqual([true]);
  });
});
