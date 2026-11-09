import type { KeyValueStorage } from "../infra/storage";

interface Counter {
  date: string;
  count: number;
}

const KEY = "hh_daily_counter";

const MOSCOW_OFFSET_MS = 3 * 60 * 60 * 1000;

// Сутки лимита — московские: сброс в полночь МСК (UTC+3, перевода часов нет)
export const moscowDate = (now: number = Date.now()) => new Date(now + MOSCOW_OFFSET_MS).toISOString().split("T")[0];

export class DailyLimit {
  constructor(
    private storage: KeyValueStorage,
    public limit: number,
    private today: () => string = () => moscowDate(),
  ) {}

  async count(): Promise<number> {
    const saved = await this.storage.get<Counter | null>(KEY, null);
    return saved?.date === this.today() ? saved.count : 0;
  }

  async increment(): Promise<number> {
    const count = (await this.count()) + 1;
    await this.storage.set<Counter>(KEY, { date: this.today(), count });
    return count;
  }

  async isReached(): Promise<boolean> {
    return (await this.count()) >= this.limit;
  }

  async remaining(): Promise<number> {
    return Math.max(0, this.limit - (await this.count()));
  }
}
