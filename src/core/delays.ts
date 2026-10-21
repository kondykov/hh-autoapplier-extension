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

export const wait = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));
