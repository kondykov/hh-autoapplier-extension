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
    queueRunLimit: 0,
    letter: "",
    server: DEFAULT_SERVER,
    importSearch: "",
    queueDryRun: true,
    rejectionReply: "Спасибо!",
  });
  await panel.mount();
  return panel;
}

// Действие человека: у программных click()/dispatchEvent isTrusted === false, подделать его может только тест
function trusted<T extends Event>(event: T): T {
  Object.defineProperty(event, "isTrusted", { value: true });
  return event;
}

const press = (el: HTMLElement) => el.dispatchEvent(trusted(new MouseEvent("click", { bubbles: true })));

function uncheck(box: HTMLInputElement) {
  box.checked = false;
  box.dispatchEvent(trusted(new Event("change")));
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

    press($("hh-btn-confirm-yes"));
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
    press($("hh-btn-confirm-no"));
    await tick();
    expect(box.checked).toBe(true);
    expect($("hh-confirm").hidden).toBe(true);
    expect(change).not.toHaveBeenCalled();
  });

  it("скрипт страницы не снимает пробный режим: программное change игнорируется", async () => {
    const change = vi.fn();
    await mount(change);
    const box = $<HTMLInputElement>("hh-input-dryrun");

    box.checked = false;
    box.dispatchEvent(new Event("change"));
    await tick();

    expect(box.checked).toBe(true);
    expect($("hh-confirm").hidden).toBe(true);
    expect(change).not.toHaveBeenCalled();
  });

  it("скрипт страницы не подтверждает: программный click() по кнопкам не отвечает на вопрос", async () => {
    const change = vi.fn();
    await mount(change);
    const box = $<HTMLInputElement>("hh-input-dryrun");

    uncheck(box);
    $("hh-btn-confirm-yes").click();
    $("hh-btn-confirm-no").click();
    await tick();

    expect($("hh-confirm").hidden).toBe(false);
    expect(box.checked).toBe(true);
    expect(change).not.toHaveBeenCalled();

    press($("hh-btn-confirm-yes"));
    await tick();
    expect(change).toHaveBeenCalledWith(false);
  });

  it("включить пробный режим можно и программно: это безопасное направление", async () => {
    const change = vi.fn();
    const panel = await mount(change);
    panel.setSettings({ queueDryRun: false, dailyLimit: 200, queueRunLimit: 0 });
    const box = $<HTMLInputElement>("hh-input-dryrun");

    box.checked = true;
    box.dispatchEvent(new Event("change"));
    expect(change).toHaveBeenCalledWith(true);
  });

  it("общий вопрос в панели: ответ, новый вопрос отменяет прежний", async () => {
    const panel = await mount();
    const first = panel.ask("Очистить аутлайн?", "Очистить");
    expect($("hh-confirm-text").textContent).toBe("Очистить аутлайн?");
    expect($("hh-btn-confirm-yes").textContent).toBe("Очистить");

    const second = panel.ask("Перейти на поиск?", "Перейти");
    expect(await first).toBe(false);
    press($("hh-btn-confirm-yes"));
    expect(await second).toBe(true);
    expect($("hh-confirm").hidden).toBe(true);
  });
});

describe("Panel: версия", () => {
  it("в заголовке — версия сборки (в тестах — dev)", async () => {
    await mount();
    expect($("hh-version").textContent).toBe("vdev");
  });
});
