// Вкладка, в которой работает content script (#51): очередь ведёт одна вкладка, остальные только показывают её.
// id знает только service worker (sender.tab), он же проверяет, открыта ли ещё вкладка-владелец
export interface TabInfo {
  // null — фон не ответил: очередь ведёт себя как раньше, без привязки к вкладке
  self: number | null;
  isOpen(id: number): Promise<boolean>;
}

export interface TabRequest {
  type: "hh-tab";
  // Открыта ли вкладка с этим id
  check?: number;
}

export interface TabResponse {
  self: number | null;
  open?: boolean;
}

export const isTabRequest = (m: unknown): m is TabRequest =>
  typeof m === "object" && m !== null && (m as { type?: unknown }).type === "hh-tab";

async function ask(request: TabRequest): Promise<TabResponse | null> {
  try {
    return (await chrome.runtime.sendMessage(request)) as TabResponse;
  } catch {
    return null;
  }
}

export async function chromeTab(): Promise<TabInfo> {
  const self = (await ask({ type: "hh-tab" }))?.self ?? null;
  return {
    self,
    // Не удалось проверить — считаем открытой: перехватить чужую живую очередь хуже, чем подождать
    isOpen: async (id) => (await ask({ type: "hh-tab", check: id }))?.open ?? true,
  };
}
