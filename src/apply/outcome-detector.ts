import { DELAYS, wait } from "../core/delays";
import { SELECTORS, TEXT_MARKERS } from "../core/selectors";
import type { Vacancy } from "../core/state";
import { getCoverLetterField } from "../dom/cover-letter";
import { detectPageType } from "../dom/page-type";
import { isAlreadyResponded, normalizeText, qs } from "../dom/query";

export type ApplyOutcome = "outline" | "modal" | "response" | "letter" | "responded" | "none";

const OUTLINE_SHORT = "ответить на несколько вопросов";
const OUTLINE_FULL = "Для отклика необходимо ответить на несколько вопросов";
// После «Отклик зарегистрирован» может последовать редирект на вопросы работодателя
const HYBRID_GRACE_MS = 2500;

const pageText = () => normalizeText(document.body.textContent);

export function isAlreadyRespondedOnPage(): boolean {
  return !!qs(SELECTORS.alreadyRespondedText) || pageText().includes(TEXT_MARKERS.alreadyResponded);
}

export function isOutlineDetected(): boolean {
  const modal = qs(SELECTORS.responseModal);
  if (modal && normalizeText(modal.textContent).includes(OUTLINE_SHORT)) return true;
  if ((qs(SELECTORS.employerAskingForTest) || qs(SELECTORS.taskBody)) && !modal) return true;
  const desc = qs(SELECTORS.titleDescription);
  if (desc && normalizeText(desc.textContent).includes(OUTLINE_SHORT)) return true;
  if (detectPageType() === "response" && pageText().includes(OUTLINE_SHORT)) return true;
  return pageText().includes(OUTLINE_FULL);
}

export const hasLetterPlace = () =>
  !!(getCoverLetterField() || qs(SELECTORS.coverLetterToggle) || qs(SELECTORS.addCoverLetterBtn));

export function detectApplyOutcome(vacancy: Vacancy | null): ApplyOutcome | null {
  if (isOutlineDetected()) return "outline";
  if (qs(SELECTORS.responseModal)) return "modal";
  if (detectPageType() === "response") return isAlreadyRespondedOnPage() ? "responded" : "response";
  if (hasLetterPlace()) return "letter";
  if (vacancy?.card && isAlreadyResponded(vacancy.card)) return "responded";
  return null;
}

// Исход подтверждается двумя одинаковыми результатами подряд
export async function waitForApplyOutcome(vacancy: Vacancy | null, timeoutMs = 8000): Promise<ApplyOutcome> {
  const start = Date.now();
  let previous: ApplyOutcome | null = null;
  while (Date.now() - start < timeoutMs) {
    const outcome = detectApplyOutcome(vacancy);
    if (outcome === "outline") return "outline";
    if (outcome && outcome === previous) {
      return outcome === "responded" ? await confirmResponded(vacancy) : outcome;
    }
    previous = outcome;
    await wait(300);
  }
  return "none";
}

async function confirmResponded(vacancy: Vacancy | null): Promise<ApplyOutcome> {
  const graceEnd = Date.now() + HYBRID_GRACE_MS;
  while (Date.now() < graceEnd) {
    await wait(DELAYS.short);
    if (isOutlineDetected()) return "outline";
  }
  return detectApplyOutcome(vacancy) === "outline" ? "outline" : "responded";
}
