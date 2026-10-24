import type { ServerSettings } from "../core/config";
import type { KeyValueStorage } from "../infra/storage";
import type { LogLevel, LogSink } from "../infra/logger";
import { parseList, type FilterRules } from "../filter/rules";
import type { ResumeProfile } from "../resume/profiles";
import { PANEL_CSS } from "./styles";

export interface PanelStats {
  applied: number;
  outline: number;
  unclear: number;
  questions: number;
  total: number;
  daily: number;
  dailyLimit: number;
}

export interface PanelHandlers {
  onCollect(): void;
  onNext(): void;
  onAuto(): void;
  onShowOutline(): void;
  onShowUnclear(): void;
  onClear(): void;
  onDownloadQuestions(): void;
  onDownloadReport(): void;
  onRejectNext(): void;
  onAutoReject(): void;
  onLetterChange(text: string): void;
  onRulesChange(rules: FilterRules): void;
  onResumesChange(profiles: ResumeProfile[]): void;
  onPresetSave(name: string): void;
  onPresetDelete(name: string): void;
  onPresetApply(name: string): void;
  onUrlChange(url: string): void;
  onLimitChange(limit: number): void;
  // apiKey: null — не менять сохранённый ключ
  onServerChange(url: string, apiKey: string | null): void;
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
      url: string;
      limit: number;
      letter: string;
      presets: string[];
      rules: FilterRules;
      resumes: ResumeProfile[];
      server: ServerSettings;
      queueDryRun: boolean;
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
      <div id="hh-autoapply-header"><span>HH AutoApply</span><button id="hh-btn-minimize" title="Свернуть">_</button></div>
      <div id="hh-autoapply-body">
        <div id="hh-captcha-banner" hidden>⚠ Капча! Авто-режим на паузе. Введите текст с картинки</div>
        <div class="hh-stats">
          ${stat("applied", "Отклики")}${stat("outline", "Аутлайн")}${stat("unclear", "Непонятное")}
          ${stat("questions", "Вопросы")}${stat("total", "Всего")}${stat("daily", "За день")}
        </div>
        <div class="hh-input-group"><label for="hh-input-url">URL поиска (редирект)</label><input id="hh-input-url" class="hh-input" /></div>
        <div class="hh-input-group"><label for="hh-input-limit">Дневной лимит</label><input id="hh-input-limit" type="number" min="1" class="hh-input" /></div>
        <div class="hh-step-info"><div class="hh-step-label">Текущий шаг</div><div class="hh-step-desc" id="hh-step-desc"></div></div>
        <div class="hh-btn-row">
          <button class="hh-btn hh-btn-primary" id="hh-btn-collect">Собрать вакансии</button>
          <button class="hh-btn hh-btn-success" id="hh-btn-next" disabled>Next →</button>
          <button class="hh-btn hh-btn-secondary" id="hh-btn-auto">Авто</button>
        </div>
        <div class="hh-btn-row">
          <button class="hh-btn hh-btn-secondary" id="hh-btn-queue" title="Одобренные вакансии с сервера">Из очереди</button>
          <label class="hh-step-label"><input type="checkbox" id="hh-input-dryrun" /> Пробный режим: не нажимать «Отправить»</label>
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
        <div class="hh-input-group"><label for="hh-input-preset">Сохранённые поиски</label>
          <div class="hh-btn-row">
            <select id="hh-input-preset" class="hh-input"></select>
            <button class="hh-btn hh-btn-secondary" id="hh-btn-preset-go">Открыть</button>
            <button class="hh-btn" id="hh-btn-preset-save">Сохранить текущий</button>
            <button class="hh-btn" id="hh-btn-preset-del">Удалить</button>
          </div>
        </div>
        <details class="hh-input-group"><summary>Фильтры вакансий</summary>
          <label for="hh-f-stop">Стоп-слова (через запятую)</label><input id="hh-f-stop" class="hh-input" />
          <label for="hh-f-required">Обязательный стек (любой из)</label><input id="hh-f-required" class="hh-input" />
          <label for="hh-f-preferred">Желательный стек (+1 балл)</label><input id="hh-f-preferred" class="hh-input" />
          <label for="hh-f-salary">Мин. оплата, ₽ (0 — выкл.)</label><input id="hh-f-salary" type="number" min="0" class="hh-input" />
          <label for="hh-f-formats">Формат (удалённо, гибрид, офис)</label><input id="hh-f-formats" class="hh-input" />
          <label for="hh-f-score">Мин. баллов (0 — выкл.)</label><input id="hh-f-score" type="number" min="0" class="hh-input" />
        </details>
        <details class="hh-input-group"><summary>Резюме по стеку (JSON)</summary>
          <textarea id="hh-input-resumes" class="hh-input" rows="8" spellcheck="false"></textarea>
          <div class="hh-step-label" id="hh-resumes-status"></div>
        </details>
        <details class="hh-input-group"><summary>Сервер (backend)</summary>
          <label for="hh-input-server-url">Адрес</label><input id="hh-input-server-url" class="hh-input" placeholder="http://127.0.0.1:8080" />
          <label for="hh-input-server-key">Ключ API (X-Api-Key)</label><input id="hh-input-server-key" type="password" class="hh-input" autocomplete="off" />
          <button class="hh-btn" id="hh-btn-server-check">Проверить связь</button>
          <div class="hh-step-label" id="hh-server-status"></div>
        </details>
        <div class="hh-input-group"><label for="hh-input-letter">Сопроводительное письмо</label><textarea id="hh-input-letter" class="hh-input" rows="5"></textarea></div>
        <div id="hh-autoapply-log"></div>
      </div>`;
  }

  private el<T extends HTMLElement>(id: string) {
    return this.root.querySelector<T>(`#${id}`)!;
  }

  private bind(): void {
    this.el("hh-btn-minimize").onclick = () => this.root.classList.toggle("minimized");
    this.el("hh-btn-collect").onclick = () => this.handlers.onCollect();
    this.el("hh-btn-next").onclick = () => this.handlers.onNext();
    this.el("hh-btn-auto").onclick = () => this.handlers.onAuto();
    this.el("hh-btn-outline").onclick = () => this.handlers.onShowOutline();
    this.el("hh-btn-unclear").onclick = () => this.handlers.onShowUnclear();
    this.el("hh-btn-clear").onclick = () => this.handlers.onClear();
    this.el("hh-btn-dlq").onclick = () => this.handlers.onDownloadQuestions();
    this.el("hh-btn-report").onclick = () => this.handlers.onDownloadReport();
    this.el("hh-btn-rej").onclick = () => this.handlers.onRejectNext();
    this.el("hh-btn-autorej").onclick = () => this.handlers.onAutoReject();
    this.el("hh-btn-queue").onclick = () => this.handlers.onQueue();

    const dryRun = this.el<HTMLInputElement>("hh-input-dryrun");
    dryRun.checked = this.initial.queueDryRun;
    dryRun.onchange = () => {
      // Выключение пробного режима — переход к настоящим откликам, только с подтверждением
      if (!dryRun.checked && !confirm("Режим «из очереди» начнёт отправлять отклики по-настоящему. Выключить пробный режим?")) {
        dryRun.checked = true;
        return;
      }
      this.handlers.onQueueDryRunChange(dryRun.checked);
    };

    const letter = this.el<HTMLTextAreaElement>("hh-input-letter");
    letter.value = this.initial.letter;
    letter.onchange = () => this.handlers.onLetterChange(letter.value);

    this.setRules(this.initial.rules);
    const resumes = this.el<HTMLTextAreaElement>("hh-input-resumes");
    resumes.value = JSON.stringify(this.initial.resumes, null, 2);
    resumes.onchange = () => {
      const status = this.el("hh-resumes-status");
      try {
        const parsed = JSON.parse(resumes.value) as ResumeProfile[];
        const valid =
          Array.isArray(parsed) &&
          parsed.every((r) => typeof r.name === "string" && typeof r.match === "string" && Array.isArray(r.keywords));
        if (!valid) throw new Error("нужен массив {name, match, keywords[]}");
        this.handlers.onResumesChange(parsed);
        status.textContent = "Сохранено";
      } catch (e) {
        status.textContent = `Ошибка: ${(e as Error).message}`;
      }
    };
    for (const id of ["stop", "required", "preferred", "salary", "formats", "score"]) {
      this.el<HTMLInputElement>(`hh-f-${id}`).onchange = () => this.handlers.onRulesChange(this.readRules());
    }

    this.setPresets(this.initial.presets);
    const preset = this.el<HTMLSelectElement>("hh-input-preset");
    this.el("hh-btn-preset-go").onclick = () => preset.value && this.handlers.onPresetApply(preset.value);
    this.el("hh-btn-preset-del").onclick = () => preset.value && this.handlers.onPresetDelete(preset.value);
    this.el("hh-btn-preset-save").onclick = () => {
      const name = prompt("Название поиска");
      if (name?.trim()) this.handlers.onPresetSave(name.trim());
    };

    const serverUrl = this.el<HTMLInputElement>("hh-input-server-url");
    const serverKey = this.el<HTMLInputElement>("hh-input-server-key");
    serverUrl.value = this.initial.server.url;
    // Панель живёт в DOM страницы hh.ru: сохранённый ключ в поле не подставляем, чтобы его не прочитали скрипты страницы
    const keyHint = (saved: boolean) => (serverKey.placeholder = saved ? "ключ сохранён — введите новый, чтобы заменить" : "не задан");
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

    const url = this.el<HTMLInputElement>("hh-input-url");
    url.value = this.initial.url;
    url.onchange = () => this.handlers.onUrlChange(url.value.trim());

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

  setNextEnabled(enabled: boolean): void {
    this.el<HTMLButtonElement>("hh-btn-next").disabled = !enabled;
  }

  setServerStatus(message: string, ok: boolean): void {
    const status = this.el("hh-server-status");
    status.textContent = message;
    status.style.color = ok ? "#4CAF50" : "#F44336";
  }

  setQueueActive(active: boolean): void {
    const btn = this.el("hh-btn-queue");
    btn.textContent = active ? "Стоп очередь" : "Из очереди";
    btn.classList.toggle("hh-btn-primary", active);
  }

  setAutoActive(active: boolean): void {
    const btn = this.el("hh-btn-auto");
    btn.textContent = active ? "Стоп" : "Авто";
    btn.classList.toggle("hh-btn-primary", active);
  }

  private readRules(): FilterRules {
    const v = (id: string) => this.el<HTMLInputElement>(`hh-f-${id}`).value;
    return {
      stopWords: parseList(v("stop")),
      requiredStack: parseList(v("required")),
      preferredStack: parseList(v("preferred")),
      minSalary: Math.max(0, parseInt(v("salary"), 10) || 0),
      formats: parseList(v("formats")),
      minScore: Math.max(0, parseInt(v("score"), 10) || 0),
    };
  }

  setRules(r: FilterRules): void {
    const set = (id: string, value: string) => (this.el<HTMLInputElement>(`hh-f-${id}`).value = value);
    set("stop", r.stopWords.join(", "));
    set("required", r.requiredStack.join(", "));
    set("preferred", r.preferredStack.join(", "));
    set("salary", String(r.minSalary));
    set("formats", r.formats.join(", "));
    set("score", String(r.minScore));
  }

  setLetter(text: string): void {
    this.el<HTMLTextAreaElement>("hh-input-letter").value = text;
  }

  setRejectActive(active: boolean): void {
    const btn = this.el("hh-btn-autorej");
    btn.textContent = active ? "Стоп отказ" : "Авто-отказ";
    btn.classList.toggle("hh-btn-primary", active);
  }

  setPresets(names: string[]): void {
    const select = this.el<HTMLSelectElement>("hh-input-preset");
    select.replaceChildren(...names.map((n) => new Option(n, n)));
  }

  setUrl(url: string): void {
    this.el<HTMLInputElement>("hh-input-url").value = url;
  }

  setStats(s: PanelStats): void {
    const set = (id: string, value: string | number) => (this.el(`hh-stat-${id}`).textContent = String(value));
    set("applied", s.applied);
    set("outline", s.outline);
    set("unclear", s.unclear);
    set("questions", s.questions);
    set("total", s.total || "-");
    set("daily", `${s.daily} / ${s.dailyLimit}`);
  }
}

