import type { ServerSettings } from "../core/config";
import type { KeyValueStorage } from "../infra/storage";
import type { LogLevel, LogSink } from "../infra/logger";
import { PANEL_CSS } from "./styles";

declare const __VERSION__: string | undefined;
// Версия сборки (scripts/build.mjs берёт её из manifest.json); в тестах сборки нет
export const VERSION = typeof __VERSION__ === "string" ? __VERSION__ : "dev";

export interface PanelStats {
  applied: number;
  outline: number;
  unclear: number;
  questions: number;
  daily: number;
  dailyLimit: number;
}

export interface PanelHandlers {
  onSendSearch(): void;
  onShowOutline(): void;
  onShowUnclear(): void;
  onClear(): void;
  onDownloadQuestions(): void;
  onDownloadReport(): void;
  onRejectNext(): void;
  onAutoReject(): void;
  // Чаты на сервер и «спасибо» на новые отказы (#26)
  onAutoReply(): void;
  onRejectionReplyChange(text: string): void;
  onLetterChange(text: string): void;
  onLimitChange(limit: number): void;
  onQueueRunLimitChange(limit: number): void;
  // apiKey: null — не менять сохранённый токен
  onServerChange(url: string, apiKey: string | null): void;
  onImportSearchChange(name: string): void;
  onServerCheck(): void;
  onQueue(): void;
  onQueueDryRunChange(enabled: boolean): void;
}

const POS_KEY = "hh_panel_pos";

export class Panel implements LogSink {
  private root = document.createElement("div");

  constructor(
    private storage: KeyValueStorage,
    private handlers: PanelHandlers,
    private initial: {
      limit: number;
      queueRunLimit: number;
      letter: string;
      server: ServerSettings;
      importSearch: string;
      queueDryRun: boolean;
      rejectionReply: string;
    },
  ) {}

  async mount(): Promise<void> {
    const style = document.createElement("style");
    style.textContent = PANEL_CSS;
    document.head.append(style);

    this.root.id = "hh-autoapply-panel";
    this.root.innerHTML = this.template();
    document.body.append(this.root);

    this.bind();
    await this.restorePosition();
  }

  private template(): string {
    const stat = (id: string, label: string) =>
      `<div class="hh-stat"><div class="hh-stat-val" id="hh-stat-${id}">0</div><div class="hh-stat-label">${label}</div></div>`;
    return `
      <div id="hh-autoapply-header"><span>HH AutoApply <span id="hh-version" title="Версия расширения">v${VERSION}</span></span><button id="hh-btn-minimize" title="Свернуть">_</button></div>
      <div id="hh-autoapply-body">
        <div id="hh-captcha-banner" hidden>⚠ Капча! Очередь на паузе. Введите текст с картинки</div>
        <div class="hh-stats">
          ${stat("applied", "Отклики")}${stat("outline", "Аутлайн")}${stat("unclear", "Непонятное")}
          ${stat("questions", "Вопросы")}${stat("daily", "За день")}
        </div>
        <div class="hh-input-group"><label for="hh-input-limit">Дневной лимит</label><input id="hh-input-limit" type="number" min="1" class="hh-input" /></div>
        <div class="hh-input-group"><label for="hh-input-run-limit">Откликов за запуск очереди (0 — без ограничения)</label><input id="hh-input-run-limit" type="number" min="0" class="hh-input" /></div>
        <div class="hh-step-info"><div class="hh-step-label">Текущий шаг</div><div class="hh-step-desc" id="hh-step-desc"></div></div>
        <div class="hh-btn-row">
          <button class="hh-btn hh-btn-primary" id="hh-btn-queue" title="Одобренные вакансии с сервера">Из очереди</button>
          <label class="hh-step-label"><input type="checkbox" id="hh-input-dryrun" /> Пробный режим: не нажимать «Отправить»</label>
        </div>
        <div class="hh-btn-row">
          <button class="hh-btn hh-btn-secondary" id="hh-btn-send-search" title="Карточки этой страницы выдачи — в поиск на сервере; отбор и оценка там">Выдачу на сервер</button>
        </div>
        <div class="hh-confirm" id="hh-confirm" hidden>
          <div id="hh-confirm-text"></div>
          <div class="hh-btn-row">
            <button class="hh-btn hh-btn-warn" id="hh-btn-confirm-yes"></button>
            <button class="hh-btn" id="hh-btn-confirm-no">Отмена</button>
          </div>
        </div>
        <div class="hh-btn-row">
          <button class="hh-btn hh-btn-warn" id="hh-btn-outline">Аутлайн</button>
          <button class="hh-btn hh-btn-warn" id="hh-btn-unclear">Непонятное</button>
          <button class="hh-btn" id="hh-btn-clear">Очистить</button>
        </div>
        <div class="hh-btn-row">
          <button class="hh-btn hh-btn-secondary" id="hh-btn-dlq">Скачать вопросы</button>
          <button class="hh-btn hh-btn-success" id="hh-btn-report">Отчёт (CSV)</button>
          <button class="hh-btn hh-btn-warn" id="hh-btn-rej">Next Отказ</button>
          <button class="hh-btn hh-btn-secondary" id="hh-btn-autorej">Авто-отказ</button>
        </div>
        <div class="hh-btn-row">
          <button class="hh-btn hh-btn-primary" id="hh-btn-autoreply" title="Чаты откликов — на сервер (статусы, разбор ответов, Telegram); на новые отказы — короткое «спасибо», где hh.ru разрешает писать">Автоответ</button>
        </div>
        <details class="hh-input-group"><summary>Сервер (backend)</summary>
          <label for="hh-input-server-url">Адрес</label><input id="hh-input-server-url" class="hh-input" placeholder="http://127.0.0.1:8080" />
          <label for="hh-input-server-key">Токен (hh_…, POST /api/tokens)</label><input id="hh-input-server-key" type="password" class="hh-input" autocomplete="off" />
          <label for="hh-input-import-search">Поиск на сервере для выдачи</label><input id="hh-input-import-search" class="hh-input" placeholder="имя поиска, например dotnet" />
          <button class="hh-btn" id="hh-btn-server-check">Проверить связь</button>
          <div class="hh-step-label" id="hh-server-status"></div>
        </details>
        <div class="hh-input-group"><label for="hh-input-rejection-reply">Ответ на отказ</label><input id="hh-input-rejection-reply" class="hh-input" /></div>
        <div class="hh-input-group"><label for="hh-input-letter">Письмо, если сервер не прислал своё</label><textarea id="hh-input-letter" class="hh-input" rows="5"></textarea></div>
        <div id="hh-autoapply-log"></div>
      </div>`;
  }

  private el<T extends HTMLElement>(id: string) {
    return this.root.querySelector<T>(`#${id}`)!;
  }

  private bind(): void {
    this.el("hh-btn-minimize").onclick = () => this.root.classList.toggle("minimized");
    this.el("hh-btn-send-search").onclick = () => this.handlers.onSendSearch();
    this.el("hh-btn-outline").onclick = () => this.handlers.onShowOutline();
    this.el("hh-btn-unclear").onclick = () => this.handlers.onShowUnclear();
    this.el("hh-btn-clear").onclick = () => this.handlers.onClear();
    this.el("hh-btn-dlq").onclick = () => this.handlers.onDownloadQuestions();
    this.el("hh-btn-report").onclick = () => this.handlers.onDownloadReport();
    this.el("hh-btn-rej").onclick = () => this.handlers.onRejectNext();
    this.el("hh-btn-autorej").onclick = () => this.handlers.onAutoReject();
    this.el("hh-btn-autoreply").onclick = () => this.handlers.onAutoReply();
    this.el("hh-btn-queue").onclick = () => this.handlers.onQueue();

    // Выключение пробного режима — переход к настоящим откликам, только с подтверждением
    const dryRun = this.el<HTMLInputElement>("hh-input-dryrun");
    dryRun.checked = this.initial.queueDryRun;
    dryRun.onchange = async () => {
      if (dryRun.checked) {
        this.handlers.onQueueDryRunChange(true);
        return;
      }
      dryRun.checked = true;
      if (!(await this.ask("Режим «из очереди» начнёт отправлять отклики по-настоящему. Выключить пробный режим?", "Да, отправлять")))
        return;
      dryRun.checked = false;
      this.handlers.onQueueDryRunChange(false);
    };

    const rejectionReply = this.el<HTMLInputElement>("hh-input-rejection-reply");
    rejectionReply.value = this.initial.rejectionReply;
    rejectionReply.onchange = () => this.handlers.onRejectionReplyChange(rejectionReply.value);

    const letter = this.el<HTMLTextAreaElement>("hh-input-letter");
    letter.value = this.initial.letter;
    letter.onchange = () => this.handlers.onLetterChange(letter.value);

    const serverUrl = this.el<HTMLInputElement>("hh-input-server-url");
    const serverKey = this.el<HTMLInputElement>("hh-input-server-key");
    serverUrl.value = this.initial.server.url;
    // Панель живёт в DOM страницы hh.ru: сохранённый токен в поле не подставляем, чтобы его не прочитали скрипты страницы
    const keyHint = (saved: boolean) => (serverKey.placeholder = saved ? "токен сохранён — введите новый, чтобы заменить" : "не задан");
    keyHint(this.initial.server.apiKey !== "");
    const saveServer = () => {
      const key = serverKey.value.trim();
      this.handlers.onServerChange(serverUrl.value.trim(), key || null);
      if (key) {
        serverKey.value = "";
        keyHint(true);
      }
    };
    serverUrl.onchange = saveServer;
    serverKey.onchange = saveServer;
    this.el("hh-btn-server-check").onclick = () => this.handlers.onServerCheck();

    const importSearch = this.el<HTMLInputElement>("hh-input-import-search");
    importSearch.value = this.initial.importSearch;
    importSearch.onchange = () => this.handlers.onImportSearchChange(importSearch.value.trim());

    const runLimit = this.el<HTMLInputElement>("hh-input-run-limit");
    runLimit.value = String(this.initial.queueRunLimit);
    runLimit.onchange = () => {
      const value = parseInt(runLimit.value, 10);
      if (value >= 0) this.handlers.onQueueRunLimitChange(value);
    };

    const limit = this.el<HTMLInputElement>("hh-input-limit");
    limit.value = String(this.initial.limit);
    limit.onchange = () => {
      const value = parseInt(limit.value, 10);
      if (value > 0) this.handlers.onLimitChange(value);
    };

    this.enableDrag();
  }

  private enableDrag(): void {
    const header = this.el("hh-autoapply-header");
    header.addEventListener("mousedown", (down) => {
      if ((down.target as HTMLElement).tagName === "BUTTON") return;
      const rect = this.root.getBoundingClientRect();
      const dx = down.clientX - rect.left;
      const dy = down.clientY - rect.top;
      Object.assign(this.root.style, { bottom: "auto", right: "auto" });

      const move = (e: MouseEvent) => {
        this.root.style.left = `${e.clientX - dx}px`;
        this.root.style.top = `${e.clientY - dy}px`;
      };
      const up = () => {
        document.removeEventListener("mousemove", move);
        document.removeEventListener("mouseup", up);
        void this.storage.set(POS_KEY, { left: this.root.style.left, top: this.root.style.top });
      };
      document.addEventListener("mousemove", move);
      document.addEventListener("mouseup", up);
    });
  }

  // Подтверждение в панели вместо confirm(): в dev-браузере Playwright сам отклоняет диалоги страницы.
  // Новый вопрос отменяет предыдущий неотвеченный
  private pendingAnswer: ((yes: boolean) => void) | null = null;

  ask(message: string, yesLabel = "Да"): Promise<boolean> {
    this.pendingAnswer?.(false);
    const box = this.el("hh-confirm");
    this.el("hh-confirm-text").textContent = message;
    this.el("hh-btn-confirm-yes").textContent = yesLabel;
    box.hidden = false;
    return new Promise((resolve) => {
      const answer = (yes: boolean) => {
        this.pendingAnswer = null;
        box.hidden = true;
        resolve(yes);
      };
      this.pendingAnswer = answer;
      this.el("hh-btn-confirm-yes").onclick = () => answer(true);
      this.el("hh-btn-confirm-no").onclick = () => answer(false);
    });
  }

  private async restorePosition(): Promise<void> {
    const pos = await this.storage.get<{ left: string; top: string } | null>(POS_KEY, null);
    if (pos) Object.assign(this.root.style, { bottom: "auto", right: "auto", left: pos.left, top: pos.top });
  }

  write(message: string, level: LogLevel): void {
    const log = this.el("hh-autoapply-log");
    const line = document.createElement("div");
    line.className = `log-${level}`;
    line.textContent = `[${new Date().toLocaleTimeString("ru-RU")}] ${message}`;
    log.append(line);
    log.scrollTop = log.scrollHeight;
  }

  setCaptcha(shown: boolean): void {
    this.el("hh-captcha-banner").hidden = !shown;
    this.root.classList.toggle("hh-captcha", shown);
  }

  setStep(text: string): void {
    this.el("hh-step-desc").textContent = text;
  }

  setServerStatus(message: string, ok: boolean): void {
    const status = this.el("hh-server-status");
    status.textContent = message;
    status.style.color = ok ? "#4CAF50" : "#F44336";
  }

  setQueueActive(active: boolean): void {
    const btn = this.el("hh-btn-queue");
    btn.textContent = active ? "Стоп очередь" : "Из очереди";
  }

  setRejectActive(active: boolean): void {
    const btn = this.el("hh-btn-autorej");
    btn.textContent = active ? "Стоп отказ" : "Авто-отказ";
    btn.classList.toggle("hh-btn-primary", active);
  }

  setStats(s: PanelStats): void {
    const set = (id: string, value: string | number) => (this.el(`hh-stat-${id}`).textContent = String(value));
    set("applied", s.applied);
    set("outline", s.outline);
    set("unclear", s.unclear);
    set("questions", s.questions);
    set("daily", `${s.daily} / ${s.dailyLimit}`);
  }
}
