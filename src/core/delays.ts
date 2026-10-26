export const DELAYS = {
  short: 100,
  click: 300,
  small: 500,
  wait: 1000,
  animation: 1500,
  step: 2000,
  retryFirst: 3000,
  retryNext: 7000,
  pause: 10000,
  longWait: 20000,
} as const;

// Разброс пауз между действиями (#19, решение владельца 2026-10-26): ±30% к базовой — средний темп прежний,
// интервалы нерегулярные, как у человека
export const JITTER = 0.3;

export const jitter = (ms: number, spread = JITTER, random: () => number = Math.random): number =>
  Math.round(ms * (1 + spread * (2 * random() - 1)));

export const wait = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));

// Пауза между действиями на странице — с разбросом. Опрос состояния и таймауты — через wait, без разброса
export const pause = (ms: number) => wait(jitter(ms));
