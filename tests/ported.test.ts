import { describe, expect, it } from "vitest";
import { nextEmptyPageAction } from "../src/core/retry-policy";
import { AppState } from "../src/core/state";
import { extractQuestions } from "../src/questions/extract";
import { isRejection } from "../src/rejects/matcher";

describe("empty page policy", () => {
  it("retries 3x, then pauses and restarts once, then stops", () => {
    const c = { emptyPageRetries: 0, emptyPageRestarts: 0 };
    expect(nextEmptyPageAction(c)).toEqual({ kind: "retry", delayMs: 3000 });
    expect(nextEmptyPageAction({ ...c, emptyPageRetries: 1 })).toEqual({ kind: "retry", delayMs: 7000 });
    expect(nextEmptyPageAction({ ...c, emptyPageRetries: 3 }).kind).toBe("pause-restart");
    expect(nextEmptyPageAction({ emptyPageRetries: 3, emptyPageRestarts: 1 }).kind).toBe("stop");
  });
});

describe("rejection matcher", () => {
  it("detects rejection phrases", () => {
    expect(isRejection("К сожалению, мы выбрали другого кандидата")).toBe(true);
    expect(isRejection("Приглашаем на интервью")).toBe(false);
  });
});

describe("questions", () => {
  it("extracts radio question with options", () => {
    document.body.innerHTML = `<div data-qa="task-body"><div data-qa="task-question">Опыт?</div>
      <label><input type="radio"><span data-qa="cell-text-content">Да</span></label>
      <label><input type="radio"><span data-qa="cell-text-content">Нет</span></label></div>`;
    expect(extractQuestions()).toEqual([{ question: "Опыт?", type: "radio", options: ["Да", "Нет"], answer: "" }]);
  });
});

describe("state persistence", () => {
  it("keeps run flags in JSON", () => {
    const s = new AppState();
    s.running = true;
    expect(s.toJSON().running).toBe(true);
  });
});
