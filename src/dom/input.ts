// Нативный setter нужен, чтобы React-форма hh.ru увидела изменение значения
export function setNativeValue(el: HTMLInputElement | HTMLTextAreaElement, text: string): void {
  const proto = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
  const setter = Object.getOwnPropertyDescriptor(proto, "value")?.set;
  if (setter) setter.call(el, text);
  else el.value = text;
  el.dispatchEvent(new Event("input", { bubbles: true, cancelable: true }));
  el.dispatchEvent(new Event("change", { bubbles: true }));
}
