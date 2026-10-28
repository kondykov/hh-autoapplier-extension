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

// Случайные интервалы, мс (решение владельца 2026-10-28 после боевого прогона: чуть дольше и случайно при кликах):
// перед каждым кликом и между вакансиями очереди — равномерно в диапазоне
export const RANGES = {
  click: [500, 1500],
  nextVacancy: [5000, 15000],
} as const satisfies Record<string, readonly [number, number]>;

export const between = ([min, max]: readonly [number, number], random: () => number = Math.random): number =>
  Math.round(min + (max - min) * random());

export const pauseBetween = (range: readonly [number, number]) => wait(between(range));
