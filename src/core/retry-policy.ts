import { DELAYS } from "../core/delays";

export type EmptyPageAction =
  | { kind: "retry"; delayMs: number }
  | { kind: "pause-restart"; delayMs: number }
  | { kind: "stop" };

export interface EmptyCounters {
  emptyPageRetries: number;
  emptyPageRestarts: number;
}

export const MAX_RETRIES = 3;

// Пустая выдача: 3 повтора (3с → 7с), затем пауза 10с и один перезапуск, затем стоп
export function nextEmptyPageAction(c: EmptyCounters): EmptyPageAction {
  if (c.emptyPageRetries < MAX_RETRIES) {
    return { kind: "retry", delayMs: c.emptyPageRetries === 0 ? DELAYS.retryFirst : DELAYS.retryNext };
  }
  if (c.emptyPageRestarts < 1) return { kind: "pause-restart", delayMs: DELAYS.pause };
  return { kind: "stop" };
}
