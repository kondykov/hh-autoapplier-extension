import { afterEach, describe, expect, it, vi } from "vitest";
import { DailyLimit, moscowDate } from "../src/core/daily-limit";
import { MemoryStorage } from "../src/infra/storage";

describe("DailyLimit", () => {
  afterEach(() => vi.useRealTimers());

  it("counts and reports limit", async () => {
    const limit = new DailyLimit(new MemoryStorage(), 2, () => "2026-01-01");
    expect(await limit.remaining()).toBe(2);
    await limit.increment();
    await limit.increment();
    expect(await limit.isReached()).toBe(true);
    expect(await limit.remaining()).toBe(0);
  });

  it("resets on a new day", async () => {
    const storage = new MemoryStorage();
    await new DailyLimit(storage, 5, () => "2026-01-01").increment();
    expect(await new DailyLimit(storage, 5, () => "2026-01-02").count()).toBe(0);
  });

  it("дата — московская: новые сутки с полуночи МСК (21:00 UTC)", () => {
    expect(moscowDate(Date.parse("2026-11-08T20:59:59Z"))).toBe("2026-11-08");
    expect(moscowDate(Date.parse("2026-11-08T21:00:00Z"))).toBe("2026-11-09");
    expect(moscowDate(Date.parse("2026-12-31T21:00:00Z"))).toBe("2027-01-01");
  });

  it("счётчик сбрасывается в полночь МСК, а не в полночь UTC", async () => {
    vi.useFakeTimers();
    const limit = new DailyLimit(new MemoryStorage(), 5);

    vi.setSystemTime(new Date("2026-11-08T20:59:00Z"));
    await limit.increment();
    expect(await limit.count()).toBe(1);

    vi.setSystemTime(new Date("2026-11-08T21:01:00Z"));
    expect(await limit.count()).toBe(0);
    await limit.increment();

    // Полночь UTC — те же московские сутки
    vi.setSystemTime(new Date("2026-11-09T00:01:00Z"));
    expect(await limit.count()).toBe(1);
  });
});
