import { isCaptchaVisible } from "../captcha/guard";
import { DELAYS, pause, wait } from "../core/delays";
import { SELECTORS } from "../core/selectors";
import { ensureCoverLetterFieldVisible, fillTextarea, getCoverLetterField } from "../dom/cover-letter";
import { detectPageType } from "../dom/page-type";
import { normalizeText, qs, qsa } from "../dom/query";
import type { Logger } from "../infra/logger";
import { isAlreadyRespondedOnPage, isOutlineDetected } from "./outcome-detector";
import { humanClick } from "../dom/input";

// dry_run — пробный режим: форма заполнена, кнопка «Отправить» найдена и активна, но не нажата;
// captcha — капча сразу после нажатия: отклик мог уйти
export type SubmitResult = "success" | "outline" | "pending_response" | "disabled" | "unclear" | "failed" | "dry_run" | "captcha";

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

async function findSubmitButton(timeoutMs = 4000): Promise<HTMLElement | null> {
  const start = Date.now();
  do {
    const modal = qs(SELECTORS.responseModal);
    const scopes: ParentNode[] = [];
    if (modal) scopes.push(modal);
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

// success — только по положительному признаку: «Вы откликнулись» или закрылась форма, открытая при клике.
// Без окна и без поля письма «формы нет» с самого начала — это не признак отправки (#50)
async function waitForSubmitResult(
  startPage: string,
  hadForm: boolean,
  timeoutMs: number,
  questionnaire: boolean,
  captchaHit: () => boolean,
): Promise<SubmitResult> {
  const start = Date.now();
  let closedStreak = 0;
  while (Date.now() - start < timeoutMs) {
    // Первой: капча вместо формы выглядит как «форма закрылась» (#78)
    if (captchaHit()) return "captcha";
    if (!questionnaire && isOutlineDetected()) return "outline";
    if (detectPageType() !== startPage) return "pending_response";
    if (isAlreadyRespondedOnPage()) return "success";
    const formOpen = qs(SELECTORS.responseModal) || getCoverLetterField();
    if (!formOpen && hadForm) {
      if (++closedStreak >= 2) return "success";
    } else {
      closedStreak = 0;
    }
    await wait(300);
  }
  return captchaHit() ? "captcha" : "unclear";
}

// Кнопка неактивна: hh.ru помечает это атрибутом disabled или aria-disabled
const isInactive = (el: HTMLElement) => (el as HTMLButtonElement).disabled || el.getAttribute("aria-disabled") === "true";

export async function attachLetterAndSubmit(
  letter: string,
  logger: Logger,
  // Пробный режим; функция — перечитывается перед самым кликом
  dryRun: boolean | (() => boolean) = false,
  // Анкета уже заполнена ответами: её вопросы на странице — не повод останавливаться
  questionnaire = false,
  // Сразу перед нажатием «Отправить»: записать, что отклик уходит, и учесть его в лимитах
  beforeClick?: () => Promise<void>,
  // Капча, пока ждём исход клика
  captchaHit: () => boolean = isCaptchaVisible,
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
    await pause(DELAYS.small);
  } else {
    logger.log("Поле письма не найдено, отправляю без письма", "warn");
  }

  if (!questionnaire && isOutlineDetected()) return "outline";

  const submit = await findSubmitButton();
  if (!submit) {
    logger.log('Кнопка "Отправить" не найдена', "warn");
    return "failed";
  }
  const real = (submit.closest("button") as HTMLButtonElement | null) ?? submit;
  if (isInactive(submit) || isInactive(real)) {
    logger.log('Кнопка "Отправить" неактивна. Возможно, нужно выбрать резюме.', "warn");
    return "disabled";
  }

  const isDry = typeof dryRun === "function" ? dryRun : () => dryRun;
  if (isDry()) {
    logger.log('Пробный режим: кнопка "Отправить" найдена и активна, не нажимаю', "warn");
    return "dry_run";
  }

  const startPage = detectPageType();
  const hadForm = !!(qs(SELECTORS.responseModal) || getCoverLetterField());
  submit.scrollIntoView({ block: "center", inline: "center" });
  if (!(await humanClick(submit, () => !isDry(), beforeClick))) {
    logger.log('Пробный режим включён перед нажатием: "Отправить" не нажата', "warn");
    return "dry_run";
  }
  logger.log('Нажата кнопка "Отправить"', "step");
  return waitForSubmitResult(startPage, hadForm, 8000, questionnaire, captchaHit);
}
