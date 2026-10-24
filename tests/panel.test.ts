import { beforeEach, describe, expect, it, vi } from "vitest";
import { DEFAULT_SERVER } from "../src/core/config";
import { EMPTY_RULES } from "../src/filter/rules";
import { MemoryStorage } from "../src/infra/storage";
import { Panel, type PanelHandlers } from "../src/ui/panel";

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;

async function mount(onQueueDryRunChange: (enabled: boolean) => void) {
  const handlers = new Proxy({ onQueueDryRunChange } as Partial<PanelHandlers>, {
    get: (target, key: string) => target[key as keyof PanelHandlers] ?? (() => {}),
  }) as PanelHandlers;
  await new Panel(new MemoryStorage(), handlers, {
    url: "",
    limit: 200,
    letter: "",
    presets: [],
    rules: EMPTY_RULES,
    resumes: [],
    server: DEFAULT_SERVER,
    queueDryRun: true,
  }).mount();
}

function uncheck(box: HTMLInputElement) {
  box.checked = false;
  box.dispatchEvent(new Event("change"));
}

describe("Panel: пробный режим очереди", () => {
  beforeEach(() => {
    document.body.innerHTML = "";
  });

  it("выключается только после подтверждения в панели, без confirm()", async () => {
    const change = vi.fn();
    const confirmSpy = vi.spyOn(window, "confirm");
    await mount(change);
    const box = $<HTMLInputElement>("hh-input-dryrun");
    expect(box.checked).toBe(true);

    uncheck(box);
    expect(box.checked).toBe(true);
    expect($("hh-dryrun-confirm").hidden).toBe(false);
    expect(change).not.toHaveBeenCalled();

    $("hh-btn-dryrun-off").click();
    expect(box.checked).toBe(false);
    expect($("hh-dryrun-confirm").hidden).toBe(true);
    expect(change).toHaveBeenCalledWith(false);
    expect(confirmSpy).not.toHaveBeenCalled();

    box.checked = true;
    box.dispatchEvent(new Event("change"));
    expect(change).toHaveBeenLastCalledWith(true);
  });

  it("«Отмена» оставляет пробный режим", async () => {
    const change = vi.fn();
    await mount(change);
    const box = $<HTMLInputElement>("hh-input-dryrun");

    uncheck(box);
    $("hh-btn-dryrun-keep").click();
    expect(box.checked).toBe(true);
    expect($("hh-dryrun-confirm").hidden).toBe(true);
    expect(change).not.toHaveBeenCalled();
  });
});
