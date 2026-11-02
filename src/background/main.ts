import { executeApiRequest, isApiRequest, type ApiResponse } from "../api/transport";
import { loadConfig } from "../core/config";
import { ChromeStorage } from "../infra/storage";
import { isTabRequest, type TabResponse } from "../infra/tab";

// Фоновый service worker: единственное место, откуда расширение ходит в backend.
// Сообщения принимаются только от своего расширения (onMessage, не onMessageExternal)
chrome.runtime.onMessage.addListener((message: unknown, sender, sendResponse: (response: ApiResponse | TabResponse) => void) => {
  if (sender.id !== chrome.runtime.id) return false;

  // id вкладки и проверка вкладки-владельца очереди (#51); chrome.tabs.get без разрешения tabs — только id и статус
  if (isTabRequest(message)) {
    const self = sender.tab?.id ?? null;
    if (message.check === undefined) {
      sendResponse({ self });
      return false;
    }
    chrome.tabs.get(message.check).then(
      () => sendResponse({ self, open: true }),
      () => sendResponse({ self, open: false }),
    );
    return true;
  }

  if (!isApiRequest(message)) return false;
  void (async () => {
    const config = await loadConfig(new ChromeStorage());
    sendResponse(await executeApiRequest(message, config.server));
  })();
  return true; // ответ асинхронный
});
