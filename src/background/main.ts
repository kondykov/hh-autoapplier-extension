import { executeApiRequest, isApiRequest, type ApiResponse } from "../api/transport";
import { loadConfig } from "../core/config";
import { ChromeStorage } from "../infra/storage";

// Фоновый service worker: единственное место, откуда расширение ходит в backend.
// Сообщения принимаются только от своего расширения (onMessage, не onMessageExternal)
chrome.runtime.onMessage.addListener((message: unknown, sender, sendResponse: (response: ApiResponse) => void) => {
  if (sender.id !== chrome.runtime.id || !isApiRequest(message)) return false;

  void (async () => {
    const config = await loadConfig(new ChromeStorage());
    sendResponse(await executeApiRequest(message, config.server));
  })();
  return true; // ответ асинхронный
});
