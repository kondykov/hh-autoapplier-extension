import { pauseBetween, RANGES } from "../core/delays";

// Нативный setter нужен, чтобы React-форма hh.ru увидела изменение значения
export function setNativeValue(el: HTMLInputElement | HTMLTextAreaElement, text: string): void {
  const proto = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
  const setter = Object.getOwnPropertyDescriptor(proto, "value")?.set;
  if (setter) setter.call(el, text);
  else el.value = text;
  el.dispatchEvent(new Event("input", { bubbles: true, cancelable: true }));
  el.dispatchEvent(new Event("change", { bubbles: true }));
}

// Клик как у человека: случайная пауза перед каждым нажатием (RANGES.click)
export async function humanClick(el: HTMLElement): Promise<void> {
  await pauseBetween(RANGES.click);
  el.click();
}
