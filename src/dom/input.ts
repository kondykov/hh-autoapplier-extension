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

// Клик как у человека: случайная пауза перед каждым нажатием (RANGES.click).
// allowed — проверка после паузы: за неё могли остановить очередь или включить пробный режим;
// before — успеть записать, что нажатие сейчас будет (страница может смениться сразу после клика)
export async function humanClick(
  el: HTMLElement,
  allowed: () => boolean = () => true,
  before?: () => Promise<void>,
): Promise<boolean> {
  await pauseBetween(RANGES.click);
  if (!allowed()) return false;
  await before?.();
  el.click();
  return true;
}
