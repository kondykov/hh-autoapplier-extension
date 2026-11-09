import { describe, expect, it } from "vitest";
import { AppState, StateRepository } from "../src/core/state";
import { MemoryStorage } from "../src/infra/storage";

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
