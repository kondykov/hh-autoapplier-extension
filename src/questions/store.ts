import type { KeyValueStorage } from "../infra/storage";
import type { QuestionnaireEntry } from "./extract";

const KEY = "hh_autoapply_collected_questions";

export class QuestionStore {
  constructor(private storage: KeyValueStorage) {}

  list(): Promise<QuestionnaireEntry[]> {
    return this.storage.get<QuestionnaireEntry[]>(KEY, []);
  }

  // Дубликаты по ссылке на вакансию не добавляются
  async add(entry: QuestionnaireEntry): Promise<boolean> {
    const all = await this.list();
    if (all.some((e) => e.vacancyUrl === entry.vacancyUrl)) return false;
    all.push(entry);
    await this.storage.set(KEY, all);
    return true;
  }

  clear(): Promise<void> {
    return this.storage.set(KEY, []);
  }
}

export function downloadJson(filename: string, data: unknown): void {
  const blob = new Blob([JSON.stringify(data, null, 2)], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.append(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}
