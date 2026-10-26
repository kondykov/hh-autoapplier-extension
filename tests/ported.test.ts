import { describe, expect, it } from "vitest";
import { AppState } from "../src/core/state";
import { extractQuestions } from "../src/questions/extract";
import { isRejection } from "../src/rejects/matcher";

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
    s.queueRunning = true;
    expect(s.toJSON().queueRunning).toBe(true);
  });
});
