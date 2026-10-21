import { DELAYS, wait } from "../core/delays";
import { SELECTORS } from "../core/selectors";
import { qs } from "./query";

export const getCoverLetterField = () =>
  qs<HTMLTextAreaElement>(SELECTORS.coverLetterInput) ??
  qs<HTMLTextAreaElement>(SELECTORS.coverLetterInputAlt);

// Нативный setter нужен, чтобы React-форма hh.ru увидела изменение значения
export function fillTextarea(textarea: HTMLTextAreaElement, text: string): void {
  textarea.focus();
  const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value")!.set!;
  setter.call(textarea, text);
  for (const type of ["input", "change", "blur"]) {
    textarea.dispatchEvent(new Event(type, { bubbles: true }));
  }
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
