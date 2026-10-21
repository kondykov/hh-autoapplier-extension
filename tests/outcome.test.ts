import { beforeEach, describe, expect, it } from "vitest";
import { detectApplyOutcome, isOutlineDetected } from "../src/apply/outcome-detector";

describe("outcome detector", () => {
  beforeEach(() => {
    document.body.innerHTML = "";
  });

  it("detects outline in modal", () => {
    document.body.innerHTML = `<div data-qa="vacancy-response-popup">Для отклика необходимо ответить на несколько вопросов</div>`;
    expect(isOutlineDetected()).toBe(true);
    expect(detectApplyOutcome(null)).toBe("outline");
  });

  it("detects regular modal", () => {
    document.body.innerHTML = `<div data-qa="vacancy-response-popup">Резюме</div>`;
    expect(detectApplyOutcome(null)).toBe("modal");
  });

  it("detects letter place", () => {
    document.body.innerHTML = `<button data-qa="vacancy-response-letter-toggle">Приложить письмо</button>`;
    expect(detectApplyOutcome(null)).toBe("letter");
  });

  it("returns null on empty page", () => {
    expect(detectApplyOutcome(null)).toBeNull();
  });
});
