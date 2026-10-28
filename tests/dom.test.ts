import { beforeEach, describe, expect, it } from "vitest";
import { loadConfig, DEFAULT_CONFIG } from "../src/core/config";
import { fillTextarea } from "../src/dom/cover-letter";
import { isAlreadyResponded, normalizeText } from "../src/dom/query";
import { MemoryStorage } from "../src/infra/storage";

describe("dom helpers", () => {
  beforeEach(() => {
    document.body.innerHTML = "";
  });

  it("detects already responded by marker and by text", () => {
    document.body.innerHTML = `
      <div id="a"><span data-qa="vacancy-serp__vacancy_responded"></span></div>
      <div id="b">Вы\u00A0откликнулись</div>
      <div id="c">nothing</div>`;
    const q = (id: string) => document.getElementById(id);
    expect(isAlreadyResponded(q("a"))).toBe(true);
    expect(isAlreadyResponded(q("b"))).toBe(true);
    expect(isAlreadyResponded(q("c"))).toBe(false);
  });

  it("normalizes text", () => {
    expect(normalizeText("a\u00A0 b\n c ")).toBe("a b c");
  });

  it("fills textarea and fires input event", () => {
    document.body.innerHTML = `<div data-qa="textarea-native-wrapper"><textarea></textarea><pre></pre></div>`;
    const ta = document.querySelector("textarea")!;
    let fired = false;
    ta.addEventListener("input", () => (fired = true));
    fillTextarea(ta, "hello");
    expect(ta.value).toBe("hello");
    expect(fired).toBe(true);
    expect(document.querySelector("pre")!.textContent).toBe("hello");
  });
});

describe("config", () => {
  it("merges saved values over defaults", async () => {
    const storage = new MemoryStorage();
    await storage.set("hh_autoapply_config", { dailyLimit: 10 });
    const cfg = await loadConfig(storage);
    expect(cfg.dailyLimit).toBe(10);
    expect(cfg.importSearch).toBe(DEFAULT_CONFIG.importSearch);
    expect(cfg.queueDryRun).toBe(true);
  });

  // #27: настройки прежнего локального отбора не возвращаются в конфиг и не сохраняются снова
  it("drops settings of the removed local filtering", async () => {
    const storage = new MemoryStorage();
    await storage.set("hh_autoapply_config", { presets: [{ name: "x" }], rules: { stopWords: ["php"] }, resumes: [], searchRedirectUrl: "u", importSearch: "dotnet" });
    const cfg = await loadConfig(storage);
    expect(Object.keys(cfg).sort()).toEqual(["coverLetterTemplate", "dailyLimit", "importSearch", "queueDryRun", "queueRunLimit", "rejectionReply", "server"]);
    expect(cfg.importSearch).toBe("dotnet");
    expect(cfg.rejectionReply).toBe(DEFAULT_CONFIG.rejectionReply);
  });
});
