import type { KeyValueStorage } from "../infra/storage";

export interface VacancyItem {
  title: string;
  link: string;
  company: string;
  salary?: string;
  format?: string;
  stack?: string[];
}

export interface Vacancy extends VacancyItem {
  text?: string;
  card: Element | null;
  applyBtn: HTMLElement | null;
}

interface PersistedState {
  outline: VacancyItem[];
  unclear: VacancyItem[];
  skippedUrls: string[];
  applied: number;
  skipped: number;
  running: boolean;
  resumeAfterRedirect: boolean;
  retryDelay: number;
  emptyPageRetries: number;
  emptyPageRestarts: number;
}

const vacancyId = (url: string) => url.match(/\/vacancy\/(\d+)|vacancyId=(\d+)/)?.slice(1).find(Boolean);

export class AppState {
  vacancies: Vacancy[] = [];
  index = 0;
  outline: VacancyItem[] = [];
  unclear: VacancyItem[] = [];
  skippedUrls: string[] = [];
  applied = 0;
  skipped = 0;
  // Авто-режим переживает перезагрузки страницы
  running = false;
  resumeAfterRedirect = false;
  retryDelay = 0;
  emptyPageRetries = 0;
  emptyPageRestarts = 0;

  isSkipped(link: string): boolean {
    if (!link) return false;
    const id = vacancyId(link);
    return this.skippedUrls.some((u) => u === link || (id !== undefined && vacancyId(u) === id));
  }

  // true, если вакансия добавлена впервые
  addOutline(item: VacancyItem): boolean {
    if (this.outline.some((o) => o.link === item.link)) return false;
    this.outline.push(item);
    if (item.link && !this.skippedUrls.includes(item.link)) this.skippedUrls.push(item.link);
    return true;
  }

  addUnclear(item: VacancyItem): void {
    if (!this.unclear.some((u) => u.link === item.link)) this.unclear.push(item);
    this.addOutline(item);
  }

  clearOutline(): void {
    this.outline = [];
    this.unclear = [];
    this.skippedUrls = [];
  }

  resetEmptyCounters(): void {
    this.retryDelay = 0;
    this.emptyPageRetries = 0;
    this.emptyPageRestarts = 0;
  }

  toJSON(): PersistedState {
    return {
      outline: this.outline,
      unclear: this.unclear,
      skippedUrls: this.skippedUrls,
      applied: this.applied,
      skipped: this.skipped,
      running: this.running,
      resumeAfterRedirect: this.resumeAfterRedirect,
      retryDelay: this.retryDelay,
      emptyPageRetries: this.emptyPageRetries,
      emptyPageRestarts: this.emptyPageRestarts,
    };
  }
}

const KEY = "hh_autoapply_state";

export class StateRepository {
  constructor(private storage: KeyValueStorage) {}

  async load(): Promise<AppState> {
    const saved = await this.storage.get<Partial<PersistedState>>(KEY, {});
    return Object.assign(new AppState(), saved);
  }

  save(state: AppState): Promise<void> {
    return this.storage.set(KEY, state.toJSON());
  }
}
