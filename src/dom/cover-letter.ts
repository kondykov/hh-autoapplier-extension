import { humanClick, setNativeValue } from "./input";
import { DELAYS, pause } from "../core/delays";
import { SELECTORS } from "../core/selectors";
import { qs, qsa } from "./query";

// Текстовые вопросы анкеты работодателя лежат в той же обёртке textarea-native-wrapper — их поле письмом не считается
export const getCoverLetterField = () =>
  qs<HTMLTextAreaElement>(SELECTORS.coverLetterInput) ??
  qsa<HTMLTextAreaElement>(SELECTORS.coverLetterInputAlt).find((t) => !t.closest(SELECTORS.taskBody)) ??
  null;

export function fillTextarea(textarea: HTMLTextAreaElement, text: string): void {
  textarea.focus();
  setNativeValue(textarea, text);
  textarea.dispatchEvent(new Event("blur", { bubbles: true }));
  // Визуальный клон magritte
  const wrapper = textarea.closest('[data-qa="textarea-native-wrapper"]') ?? textarea.parentElement;
  const clone = wrapper?.querySelector("pre");
  if (clone) clone.textContent = text;
}

export async function ensureCoverLetterFieldVisible(): Promise<HTMLTextAreaElement | null> {
  const toggle = qs(SELECTORS.coverLetterToggle) ?? qs(SELECTORS.addCoverLetterBtn);
  if (!toggle) return getCoverLetterField();

  const target = (toggle.closest('button, [role="button"], label') ?? toggle) as HTMLElement;
  target.scrollIntoView?.({ block: "center", inline: "center", behavior: "auto" });
  for (let attempt = 0; attempt < 2; attempt++) {
    await humanClick(target);
    await pause(DELAYS.animation);
    const field = getCoverLetterField();
    if (field) return field;
  }
  return getCoverLetterField();
}
