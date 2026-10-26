import { beforeEach, describe, expect, it, vi } from "vitest";
import { DEFAULT_SERVER } from "../src/core/config";
import { MemoryStorage } from "../src/infra/storage";
import { Panel, type PanelHandlers } from "../src/ui/panel";

const tick = () => new Promise((r) => setTimeout(r, 0));

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;

async function mount(onQueueDryRunChange: (enabled: boolean) => void = () => {}) {
  const handlers = new Proxy({ onQueueDryRunChange } as Partial<PanelHandlers>, {
    get: (target, key: string) => target[key as keyof PanelHandlers] ?? (() => {}),
  }) as PanelHandlers;
  const panel = new Panel(new MemoryStorage(), handlers, {
    limit: 200,
    letter: "",
    server: DEFAULT_SERVER,
    importSearch: "",
    queueDryRun: true,
  });
  await panel.mount();
  return panel;
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
    expect($("hh-confirm").hidden).toBe(false);
    expect(change).not.toHaveBeenCalled();

    $("hh-btn-confirm-yes").click();
    await tick();
    expect(box.checked).toBe(false);
    expect($("hh-confirm").hidden).toBe(true);
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
    $("hh-btn-confirm-no").click();
    await tick();
    expect(box.checked).toBe(true);
    expect($("hh-confirm").hidden).toBe(true);
    expect(change).not.toHaveBeenCalled();
  });

  it("общий вопрос в панели: ответ, новый вопрос отменяет прежний", async () => {
    const panel = await mount();
    const first = panel.ask("Очистить аутлайн?", "Очистить");
    expect($("hh-confirm-text").textContent).toBe("Очистить аутлайн?");
    expect($("hh-btn-confirm-yes").textContent).toBe("Очистить");

    const second = panel.ask("Перейти на поиск?", "Перейти");
    expect(await first).toBe(false);
    $("hh-btn-confirm-yes").click();
    expect(await second).toBe(true);
    expect($("hh-confirm").hidden).toBe(true);
  });
});
