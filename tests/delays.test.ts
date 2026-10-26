import { describe, expect, it } from "vitest";
import { DELAYS, JITTER, jitter } from "../src/core/delays";

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
