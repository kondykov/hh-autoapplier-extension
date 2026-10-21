import type { KeyValueStorage } from "../infra/storage";

export interface ApplicationRecord {
  title: string;
  stack: string;
  company: string;
  salary: string;
  format: string;
  link: string;
  appliedAt: string;
}

const KEY = "hh_autoapply_applications";

export class ApplicationStore {
  constructor(private storage: KeyValueStorage) {}

  list(): Promise<ApplicationRecord[]> {
    return this.storage.get<ApplicationRecord[]>(KEY, []);
  }

  // Повторный отклик на ту же вакансию не дублируется
  async add(record: ApplicationRecord): Promise<void> {
    const all = await this.list();
    if (record.link && all.some((r) => r.link === record.link)) return;
    all.push(record);
    await this.storage.set(KEY, all);
  }

  clear(): Promise<void> {
    return this.storage.set(KEY, []);
  }
}

const HEADERS = ["Название вакансии", "Стек", "Компания", "Оплата труда", "Формат трудоустройства", "Ссылка", "Дата"];

const cell = (v: string) => `"${v.replace(/"/g, '""')}"`;

// ; и BOM, чтобы Excel в русской локали открыл файл сразу в колонках и в UTF-8
export function toCsv(records: ApplicationRecord[]): string {
  const rows = records.map((r) =>
    [r.title, r.stack, r.company, r.salary || "не указана", r.format || "не указан", r.link, r.appliedAt.slice(0, 10)]
      .map(cell)
      .join(";"),
  );
  return "\uFEFF" + [HEADERS.map(cell).join(";"), ...rows].join("\r\n");
}

export function downloadCsv(filename: string, csv: string): void {
  const url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8" }));
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.append(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}
