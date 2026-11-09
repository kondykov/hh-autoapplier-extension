// Замена GM_getValue/GM_setValue. Интерфейс позволяет подменять хранилище в тестах (DIP)
export interface KeyValueStorage {
  get<T>(key: string, fallback: T): Promise<T>;
  set<T>(key: string, value: T): Promise<void>;
  // Запись ключа из другой вкладки (#51). Свои записи не приходят: событие о них может прийти позже следующей правки
  watch(key: string, onChange: (value: unknown) => void): void;
}

// Свои последние записи по ключу: событие с таким же значением — эхо своей записи, а не чужая правка
class OwnWrites {
  private recent = new Map<string, string[]>();

  remember(key: string, value: unknown): void {
    const list = this.recent.get(key) ?? [];
    list.push(JSON.stringify(value));
    if (list.length > 50) list.shift();
    this.recent.set(key, list);
  }

  isOwn(key: string, value: unknown): boolean {
    return this.recent.get(key)?.includes(JSON.stringify(value)) ?? false;
  }
}

export class ChromeStorage implements KeyValueStorage {
  private own = new OwnWrites();

  async get<T>(key: string, fallback: T): Promise<T> {
    const res = await chrome.storage.local.get(key);
    return key in res ? (res[key] as T) : fallback;
  }

  async set<T>(key: string, value: T): Promise<void> {
    this.own.remember(key, value);
    await chrome.storage.local.set({ [key]: value });
  }

  watch(key: string, onChange: (value: unknown) => void): void {
    chrome.storage.onChanged.addListener((changes, area) => {
      if (area !== "local" || !(key in changes)) return;
      const value = changes[key].newValue;
      if (!this.own.isOwn(key, value)) onChange(value);
    });
  }
}

type Listener = { key: string; source: MemoryStorage; onChange: (value: unknown) => void };

// В тестах: fork() — вторая вкладка над теми же данными, со своими записями и подписками
export class MemoryStorage implements KeyValueStorage {
  private own = new OwnWrites();

  constructor(
    private data = new Map<string, unknown>(),
    private listeners: Listener[] = [],
  ) {}

  fork(): MemoryStorage {
    return new MemoryStorage(this.data, this.listeners);
  }

  async get<T>(key: string, fallback: T): Promise<T> {
    return this.data.has(key) ? (structuredClone(this.data.get(key)) as T) : fallback;
  }

  async set<T>(key: string, value: T): Promise<void> {
    const copy = structuredClone(value);
    this.own.remember(key, copy);
    this.data.set(key, copy);
    for (const l of this.listeners)
      if (l.key === key && !l.source.own.isOwn(key, copy)) l.onChange(structuredClone(copy));
  }

  watch(key: string, onChange: (value: unknown) => void): void {
    this.listeners.push({ key, source: this, onChange });
  }
}
