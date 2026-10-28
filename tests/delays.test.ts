import { describe, expect, it, vi } from "vitest";
import { between, DELAYS, JITTER, jitter, RANGES } from "../src/core/delays";
import { humanClick } from "../src/dom/input";

describe("разброс пауз (#19)", () => {
  it("±30% к базовой: крайние значения и середина", () => {
    expect(jitter(1000, JITTER, () => 0)).toBe(700);
    expect(jitter(1000, JITTER, () => 0.5)).toBe(1000);
    expect(jitter(1000, JITTER, () => 0.999999)).toBe(1300);
  });

  it("всегда в пределах ±30%, в среднем — прежний темп", () => {
    const samples = Array.from({ length: 2000 }, () => jitter(DELAYS.step));
    expect(Math.min(...samples)).toBeGreaterThanOrEqual(DELAYS.step * 0.7);
    expect(Math.max(...samples)).toBeLessThanOrEqual(DELAYS.step * 1.3);
    const mean = samples.reduce((sum, ms) => sum + ms, 0) / samples.length;
    expect(Math.abs(mean - DELAYS.step) / DELAYS.step).toBeLessThan(0.05);
    // Не одна и та же пауза: интервалы нерегулярные
    expect(new Set(samples).size).toBeGreaterThan(100);
  });
});

describe("случайные интервалы (решение владельца 2026-10-28)", () => {
  it("равномерно в диапазоне: края и середина", () => {
    expect(between([500, 1500], () => 0)).toBe(500);
    expect(between([500, 1500], () => 0.5)).toBe(1000);
    expect(between([500, 1500], () => 0.999999)).toBe(1500);
  });

  it("клик 0,5–1,5 с, между вакансиями 5–15 с, интервалы разные", () => {
    for (const range of [RANGES.click, RANGES.nextVacancy]) {
      const samples = Array.from({ length: 2000 }, () => between(range));
      expect(Math.min(...samples)).toBeGreaterThanOrEqual(range[0]);
      expect(Math.max(...samples)).toBeLessThanOrEqual(range[1]);
      expect(new Set(samples).size).toBeGreaterThan(100);
    }
    expect(RANGES.click).toEqual([500, 1500]);
    expect(RANGES.nextVacancy).toEqual([5000, 15000]);
  });

  it("humanClick ждёт перед нажатием", async () => {
    vi.useFakeTimers();
    try {
      const button = document.createElement("button");
      const onClick = vi.fn();
      button.onclick = onClick;
      const clicked = humanClick(button);
      await vi.advanceTimersByTimeAsync(RANGES.click[0] - 1);
      expect(onClick).not.toHaveBeenCalled();
      await vi.advanceTimersByTimeAsync(RANGES.click[1]);
      await clicked;
      expect(onClick).toHaveBeenCalledTimes(1);
    } finally {
      vi.useRealTimers();
    }
  });
});
