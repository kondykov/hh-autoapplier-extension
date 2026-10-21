import type { KeyValueStorage } from "../infra/storage";

interface Counter {
  date: string;
  count: number;
}

const KEY = "hh_daily_counter";

export class DailyLimit {
  constructor(
    private storage: KeyValueStorage,
    private limit: number,
    private today: () => string = () => new Date().toISOString().split("T")[0],
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
