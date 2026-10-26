import { describe, expect, it } from "vitest";
import { AppState, StateRepository } from "../src/core/state";
import { MemoryStorage } from "../src/infra/storage";

describe("AppState", () => {
  it("dedups outline", () => {
    const s = new AppState();
    const item = { title: "QA", link: "https://hh.ru/vacancy/123?from=x", company: "A" };
    expect(s.addOutline(item)).toBe(true);
    expect(s.addOutline(item)).toBe(false);
  });

  it("unclear also lands in outline", () => {
    const s = new AppState();
    s.addUnclear({ title: "QA", link: "https://hh.ru/vacancy/5", company: "B" });
    expect(s.unclear).toHaveLength(1);
    expect(s.outline).toHaveLength(1);
  });
});

describe("StateRepository", () => {
  // #27: флаги прежнего авто-режима из сохранённого состояния не подхватываются
  it("loads only known fields", async () => {
    const storage = new MemoryStorage();
    await storage.set("hh_autoapply_state", { running: true, resumeAfterRedirect: true, applied: 3, queueRunning: true });
    const state = await new StateRepository(storage).load();
    expect(state.applied).toBe(3);
    expect(state.queueRunning).toBe(true);
    expect(Object.keys(state.toJSON())).not.toContain("running");
    expect("running" in state).toBe(false);
  });
});
