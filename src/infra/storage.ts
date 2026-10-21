// Замена GM_getValue/GM_setValue. Интерфейс позволяет подменять хранилище в тестах (DIP)
export interface KeyValueStorage {
  get<T>(key: string, fallback: T): Promise<T>;
  set<T>(key: string, value: T): Promise<void>;
}

export class ChromeStorage implements KeyValueStorage {
  async get<T>(key: string, fallback: T): Promise<T> {
    const res = await chrome.storage.local.get(key);
    return key in res ? (res[key] as T) : fallback;
  }

  async set<T>(key: string, value: T): Promise<void> {
    await chrome.storage.local.set({ [key]: value });
  }
}

export class MemoryStorage implements KeyValueStorage {
  private data = new Map<string, unknown>();

  async get<T>(key: string, fallback: T): Promise<T> {
    return this.data.has(key) ? (this.data.get(key) as T) : fallback;
  }

  async set<T>(key: string, value: T): Promise<void> {
    this.data.set(key, value);
  }
}
