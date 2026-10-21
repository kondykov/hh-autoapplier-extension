import { describe, expect, it } from "vitest";
import { AppState } from "../src/core/state";

describe("AppState", () => {
  it("dedups outline and remembers skipped by vacancy id", () => {
    const s = new AppState();
    const item = { title: "QA", link: "https://hh.ru/vacancy/123?from=x", company: "A" };
    expect(s.addOutline(item)).toBe(true);
    expect(s.addOutline(item)).toBe(false);
    expect(s.isSkipped("https://tver.hh.ru/vacancy/123?query=y")).toBe(true);
    expect(s.isSkipped("https://hh.ru/vacancy/999")).toBe(false);
  });

  it("unclear also lands in outline", () => {
    const s = new AppState();
    s.addUnclear({ title: "QA", link: "https://hh.ru/vacancy/5", company: "B" });
    expect(s.unclear).toHaveLength(1);
    expect(s.outline).toHaveLength(1);
  });
});
