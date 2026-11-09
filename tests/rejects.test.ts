import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Logger } from "../src/infra/logger";
import { openNextRejection } from "../src/rejects/runner";

const REJECT = "К сожалению, мы выбрали другого кандидата";

// Значок чата в списке; link — ссылка на чат вокруг него. Каждый вызов — новые DOM-элементы, как при перерисовке списка
function renderList(chats: { link?: string; text?: string }[]) {
  document.body.innerHTML = chats
    .map(({ link, text = REJECT }) => {
      const badge = `<div data-qa="chatik-info-badges">${text}</div>`;
      return link ? `<a href="${link}">${badge}</a>` : `<div>${badge}</div>`;
    })
    .join("");
  const opened: string[] = [];
  for (const [i, badge] of Array.from(document.querySelectorAll<HTMLElement>('[data-qa="chatik-info-badges"]')).entries())
    badge.addEventListener("click", () => opened.push(chats[i].link ?? String(i)));
  return opened;
}

describe("openNextRejection", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  const next = async () => {
    const result = openNextRejection(new Logger());
    await vi.runAllTimersAsync();
    return result;
  };

  // Ключи живут в модуле, пока жива страница: у каждого теста свои чаты
  it("перерисовка списка — тот же чат не открывается снова", async () => {
    const opened = renderList([{ link: "/chat/1001" }]);
    expect(await next()).toBe(true);
    expect(opened).toEqual(["/chat/1001"]);

    const redrawn = renderList([{ link: "/chat/1001?hhtmFrom=list" }]);
    expect(await next()).toBe(false);
    expect(redrawn).toEqual([]);
  });

  it("одинаковый текст в разных чатах — открываются оба", async () => {
    renderList([{ link: "/chat/2001" }, { link: "/chat/2002" }]);
    expect(await next()).toBe(true);

    const redrawn = renderList([{ link: "/chat/2001" }, { link: "/chat/2002" }]);
    expect(await next()).toBe(true);
    expect(redrawn).toEqual(["/chat/2002"]);
  });

  it("без ссылки на чат — определяется по тексту значка", async () => {
    const text = `${REJECT} — без ссылки`;
    renderList([{ text }]);
    expect(await next()).toBe(true);

    const redrawn = renderList([{ text }]);
    expect(await next()).toBe(false);
    expect(redrawn).toEqual([]);
  });

  it("не отказ — не открывается", async () => {
    const opened = renderList([{ link: "/chat/3001", text: "Приглашаем на интервью" }]);
    expect(await next()).toBe(false);
    expect(opened).toEqual([]);
  });
});
