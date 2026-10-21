import { setNativeValue } from "./input";
import { DELAYS, wait } from "../core/delays";
import { SELECTORS } from "../core/selectors";
import { qs } from "./query";

export const getCoverLetterField = () =>
  qs<HTMLTextAreaElement>(SELECTORS.coverLetterInput) ??
  qs<HTMLTextAreaElement>(SELECTORS.coverLetterInputAlt);

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
  await wait(DELAYS.short);
  for (let attempt = 0; attempt < 2; attempt++) {
    target.click();
    await wait(DELAYS.animation);
    const field = getCoverLetterField();
    if (field) return field;
  }
  return getCoverLetterField();
}
