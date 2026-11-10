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
  // Часть названия резюме на hh.ru (match профиля с сервера): по ней резюме выбирается в форме отклика
  resumeMatch?: string;
  // Готовое письмо с backend
  letter?: string;
  // Ответы на анкету работодателя с backend
  answers?: QuestionAnswer[];
}

interface PersistedState {
  applied: number;
  queueRunning: boolean;
  queueCurrent: QueueItem | null;
  queueTried: string[];
  queueRunApplied: number;
  queueFailStreak: number;
  queueOpened: boolean;
  queueSubmitted: string | null;
  queueTab: number | null;
  queueRunId: string | null;
  autoReplies: AutoReply[];
}

export class AppState {
  applied = 0;
  // Режим «из очереди» переживает переходы между страницами отклика
  queueRunning = false;
  queueCurrent: QueueItem | null = null;
  // id, уже опробованные в этом запуске: в пробном режиме сервер о них не знает и вернёт их снова
  queueTried: string[] = [];
  // Откликов, ушедших на hh.ru в этом запуске очереди: для лимита за запуск
  queueRunApplied = 0;
  // Неудачных вакансий подряд в этом запуске: при QUEUE_FAIL_LIMIT очередь останавливается (#75)
  queueFailStreak = 0;
  // Переход к форме queueCurrent уже начат. hh.ru после «Отправить» сам загружает новую страницу — она может прийти
  // раньше нашего перехода и не должна считать, что форма следующей вакансии не открылась
  queueOpened = false;
  // «Отправить» по этой вакансии уже нажата (#52): hh.ru мог загрузить новую страницу раньше, чем пришёл исход, —
  // такая загрузка — не «форма не открылась», а отклик, который, скорее всего, ушёл
  queueSubmitted: string | null = null;
  // Вкладка, которая ведёт очередь (#51); остальные вкладки hh.ru её только показывают
  queueTab: number | null = null;
  // Запуск очереди: остановка из другой вкладки относится только к своему запуску, не к следующему
  queueRunId: string | null = null;
  // Автоответы на отказы, где можно писать: переживают переход между чатами
  autoReplies: AutoReply[] = [];

  toJSON(): PersistedState {
    return {
      applied: this.applied,
      queueRunning: this.queueRunning,
      queueCurrent: this.queueCurrent,
      queueTried: this.queueTried,
      queueRunApplied: this.queueRunApplied,
      queueFailStreak: this.queueFailStreak,
      queueOpened: this.queueOpened,
      queueSubmitted: this.queueSubmitted,
      queueTab: this.queueTab,
      queueRunId: this.queueRunId,
      autoReplies: this.autoReplies,
    };
  }
}

const KEY = "hh_autoapply_state";

export class StateRepository {
  constructor(private storage: KeyValueStorage) {}

  // Только известные поля: флаги прежнего авто-режима из старого состояния не подхватываются
  async load(): Promise<AppState> {
    return fromSaved(await this.storage.get<Partial<PersistedState>>(KEY, {}));
  }

  save(state: AppState): Promise<void> {
    return this.storage.set(KEY, state.toJSON());
  }

  // Состояние, записанное другой вкладкой (#51)
  watch(onChange: (saved: AppState) => void): void {
    this.storage.watch(KEY, (value) => onChange(fromSaved((value ?? {}) as Partial<PersistedState>)));
  }
}

function fromSaved(saved: Partial<PersistedState>): AppState {
  const state = new AppState();
  state.applied = saved.applied ?? 0;
  state.queueRunning = saved.queueRunning ?? false;
  state.queueCurrent = saved.queueCurrent ?? null;
  state.queueTried = saved.queueTried ?? [];
  state.queueRunApplied = saved.queueRunApplied ?? 0;
  state.queueFailStreak = saved.queueFailStreak ?? 0;
  state.queueOpened = saved.queueOpened ?? false;
  state.queueSubmitted = saved.queueSubmitted ?? null;
  state.queueTab = saved.queueTab ?? null;
  state.queueRunId = saved.queueRunId ?? null;
  state.autoReplies = saved.autoReplies ?? [];
  return state;
}
