import { DELAYS, wait } from "../core/delays";
import { SELECTORS } from "../core/selectors";
import type { Vacancy } from "../core/state";
import { ensureCoverLetterFieldVisible, fillTextarea, getCoverLetterField } from "../dom/cover-letter";
import { detectPageType } from "../dom/page-type";
import { isAlreadyResponded, normalizeText, qs, qsa } from "../dom/query";
import type { Logger } from "../infra/logger";
import { isAlreadyRespondedOnPage, isOutlineDetected } from "./outcome-detector";

// dry_run — пробный режим: форма заполнена, кнопка «Отправить» найдена и активна, но не нажата
export type SubmitResult = "success" | "outline" | "pending_response" | "disabled" | "unclear" | "failed" | "dry_run";

function findButtonByText(root: ParentNode, text: string, excludeApply: boolean): HTMLElement | null {
  const isApply = (el: Element) => excludeApply && el.matches(SELECTORS.applyButton);
  const nodes = qsa<HTMLElement>('button, [role="button"], a', root);
  const exact = nodes.find((n) => !isApply(n) && normalizeText(n.textContent) === text);
  if (exact) return exact;
  const label = qsa<HTMLElement>("span", root).find(
    (s) => !s.children.length && normalizeText(s.textContent) === text && !(excludeApply && s.closest(SELECTORS.applyButton)),
  );
  if (label) return (label.closest('button, [role="button"]') as HTMLElement | null) ?? label;
  return nodes.find((n) => !isApply(n) && normalizeText(n.textContent).includes(text)) ?? null;
}

async function findSubmitButton(scope: Element | null, timeoutMs = 4000): Promise<HTMLElement | null> {
  const start = Date.now();
  do {
    const modal = qs(SELECTORS.responseModal);
    const scopes: ParentNode[] = [];
    if (modal) scopes.push(modal);
    if (scope && scope !== modal) scopes.push(scope);
    scopes.push(document.body);

    for (const s of scopes) {
      const byQa = qs<HTMLElement>(SELECTORS.submitButton, s);
      if (byQa) return byQa;
    }
    for (const s of scopes) {
      const bySend = findButtonByText(s, "Отправить", false);
      if (bySend) return bySend;
    }
    for (const s of scopes) {
      const byApply = findButtonByText(s, "Откликнуться", true);
      if (byApply) return byApply;
    }
    if (Date.now() - start >= timeoutMs) return null;
    await wait(200);
  } while (true);
}

async function waitForSubmitResult(
  vacancy: Vacancy | null,
  startPage: string,
  hadModal: boolean,
  timeoutMs: number,
  questionnaire: boolean,
): Promise<SubmitResult> {
  const start = Date.now();
  let closedStreak = 0;
  while (Date.now() - start < timeoutMs) {
    if (!questionnaire && isOutlineDetected()) return "outline";
    if (detectPageType() !== startPage) return "pending_response";
    if (isAlreadyRespondedOnPage()) return "success";
    if (vacancy?.card && isAlreadyResponded(vacancy.card)) return "success";
    const formOpen = qs(SELECTORS.responseModal) || getCoverLetterField();
    if (!formOpen && (hadModal || startPage !== "search")) {
      if (++closedStreak >= 2) return "success";
    } else {
      closedStreak = 0;
    }
    await wait(300);
  }
  return "unclear";
}

export async function attachLetterAndSubmit(
  vacancy: Vacancy | null,
  letter: string,
  logger: Logger,
  dryRun = false,
  // Анкета уже заполнена ответами: её вопросы на странице — не повод останавливаться
  questionnaire = false,
): Promise<SubmitResult> {
  if (!questionnaire && isOutlineDetected()) return "outline";

  let textarea = getCoverLetterField() ?? (await ensureCoverLetterFieldVisible());
  for (let i = 0; i < 8 && !textarea; i++) {
    await wait(250);
    textarea = getCoverLetterField();
  }

  if (textarea) {
    fillTextarea(textarea, letter);
    logger.log("Сопроводительное письмо вставлено", "success");
    await wait(DELAYS.small);
  } else {
    logger.log("Поле письма не найдено, отправляю без письма", "warn");
  }

  if (!questionnaire && isOutlineDetected()) return "outline";

  const submit = await findSubmitButton(vacancy?.card ?? null);
  if (!submit) {
    logger.log('Кнопка "Отправить" не найдена', "warn");
    return "failed";
  }
  const real = (submit.closest("button") as HTMLButtonElement | null) ?? submit;
  if ((submit as HTMLButtonElement).disabled || (real as HTMLButtonElement).disabled) {
    logger.log('Кнопка "Отправить" неактивна. Возможно, нужно выбрать резюме.', "warn");
    return "disabled";
  }

  if (dryRun) {
    logger.log('Пробный режим: кнопка "Отправить" найдена и активна, не нажимаю', "warn");
    return "dry_run";
  }

  const startPage = detectPageType();
  const hadModal = !!qs(SELECTORS.responseModal);
  submit.scrollIntoView({ block: "center", inline: "center" });
  await wait(DELAYS.short);
  submit.click();
  logger.log('Нажата кнопка "Отправить"', "step");
  return waitForSubmitResult(vacancy, startPage, hadModal, 8000, questionnaire);
}
