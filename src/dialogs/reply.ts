import { DELAYS, pause, wait } from "../core/delays";
import { SELECTORS } from "../core/selectors";
import { setNativeValue } from "../dom/input";
import { normalizeText, qs } from "../dom/query";

export type SendResult = { ok: true } | { ok: false; error: string };

export const chatUrl = (chatId: number, origin = location.origin) => `${origin}/chat/${chatId}`;

async function waitFor<T>(probe: () => T | null | undefined, timeoutMs: number, stepMs = DELAYS.small): Promise<T | null> {
  const until = Date.now() + timeoutMs;
  for (;;) {
    const found = probe();
    if (found) return found;
    if (Date.now() >= until) return null;
    await wait(stepMs);
  }
}

// Сообщение в открытый чат hh.ru: вставить в поле и отправить (кнопкой или Enter). Ушло — поле очистилось
export async function sendChatMessage(text: string, timeoutMs = DELAYS.longWait): Promise<SendResult> {
  // Предупреждение «переписка недоступна» вместо поля — ждать дальше незачем
  const found = await waitFor(() => qs<HTMLTextAreaElement>(SELECTORS.chatInput) ?? (qs(SELECTORS.chatWarning) ? ("closed" as const) : null), timeoutMs);
  const input = found === "closed" ? null : found;
  if (!input) {
    const warning = normalizeText(qs(SELECTORS.chatWarning)?.textContent);
    return { ok: false, error: warning || "поле сообщения не найдено" };
  }

  input.focus();
  setNativeValue(input, text);
  await pause(DELAYS.small);
  const send = qs<HTMLButtonElement>(SELECTORS.chatSend);
  if (send && !send.disabled) send.click();
  else input.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", code: "Enter", keyCode: 13, bubbles: true, cancelable: true }));

  const cleared = await waitFor(() => (qs<HTMLTextAreaElement>(SELECTORS.chatInput)?.value ?? "") === "" || undefined, DELAYS.retryNext);
  return cleared ? { ok: true } : { ok: false, error: "сообщение не ушло: поле не очистилось" };
}
