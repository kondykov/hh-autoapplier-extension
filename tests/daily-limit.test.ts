import { describe, expect, it } from "vitest";
import { DailyLimit } from "../src/core/daily-limit";
import { MemoryStorage } from "../src/infra/storage";

describe("DailyLimit", () => {
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
});
