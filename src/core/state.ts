import type { AutoReply } from "../api/client";
import type { KeyValueStorage } from "../infra/storage";
import type { QuestionAnswer } from "../questions/fill";
import type { QueueItem } from "../queue/queue-mode";

export interface VacancyItem {
  title: string;
  link: string;
  company: string;
  salary?: string;
  format?: string;
  stack?: string[];
  resume?: string;
}

export interface Vacancy extends VacancyItem {
  // Подстрока названия резюме на hh.ru (match профиля с сервера): по ней резюме выбирается в форме отклика
  resumeMatch?: string;
  // Готовое письмо с backend
  letter?: string;
  // Ответы на анкету работодателя с backend
  answers?: QuestionAnswer[];
}

interface PersistedState {
  outline: VacancyItem[];
  unclear: VacancyItem[];
  applied: number;
  skipped: number;
  queueRunning: boolean;
  queueCurrent: QueueItem | null;
  queueTried: string[];
  queueRunApplied: number;
  queueOpened: boolean;
  autoReplies: AutoReply[];
}

export class AppState {
  outline: VacancyItem[] = [];
  unclear: VacancyItem[] = [];
  applied = 0;
  skipped = 0;
  // Режим «из очереди» переживает переходы между страницами отклика
  queueRunning = false;
  queueCurrent: QueueItem | null = null;
  // id, уже опробованные в этом запуске: в пробном режиме сервер о них не знает и вернёт их снова
  queueTried: string[] = [];
  // Откликов, ушедших на hh.ru в этом запуске очереди: для лимита за запуск
  queueRunApplied = 0;
  // Переход к форме queueCurrent уже начат. hh.ru после «Отправить» сам загружает новую страницу — она может прийти
  // раньше нашего перехода и не должна считать, что форма следующей вакансии не открылась
  queueOpened = false;
  // Автоответы на отказы, где можно писать: переживают переход между чатами
  autoReplies: AutoReply[] = [];

  // true, если вакансия добавлена впервые
  addOutline(item: VacancyItem): boolean {
    if (this.outline.some((o) => o.link === item.link)) return false;
    this.outline.push(item);
    return true;
  }

  addUnclear(item: VacancyItem): void {
    if (!this.unclear.some((u) => u.link === item.link)) this.unclear.push(item);
    this.addOutline(item);
  }

  clearOutline(): void {
    this.outline = [];
    this.unclear = [];
  }

  toJSON(): PersistedState {
    return {
      outline: this.outline,
      unclear: this.unclear,
      applied: this.applied,
      skipped: this.skipped,
      queueRunning: this.queueRunning,
      queueCurrent: this.queueCurrent,
      queueTried: this.queueTried,
      queueRunApplied: this.queueRunApplied,
      queueOpened: this.queueOpened,
      autoReplies: this.autoReplies,
    };
  }
}

const KEY = "hh_autoapply_state";

export class StateRepository {
  constructor(private storage: KeyValueStorage) {}

  // Только известные поля: флаги прежнего авто-режима из старого состояния не подхватываются
  async load(): Promise<AppState> {
    const saved = await this.storage.get<Partial<PersistedState>>(KEY, {});
    const state = new AppState();
    state.outline = saved.outline ?? [];
    state.unclear = saved.unclear ?? [];
    state.applied = saved.applied ?? 0;
    state.skipped = saved.skipped ?? 0;
    state.queueRunning = saved.queueRunning ?? false;
    state.queueCurrent = saved.queueCurrent ?? null;
    state.queueTried = saved.queueTried ?? [];
    state.queueRunApplied = saved.queueRunApplied ?? 0;
    state.queueOpened = saved.queueOpened ?? false;
    state.autoReplies = saved.autoReplies ?? [];
    return state;
  }

  save(state: AppState): Promise<void> {
    return this.storage.set(KEY, state.toJSON());
  }
}
