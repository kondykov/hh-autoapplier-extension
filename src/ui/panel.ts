import type { KeyValueStorage } from "../infra/storage";
import type { LogLevel, LogSink } from "../infra/logger";
import { PANEL_CSS } from "./styles";

export interface PanelStats {
  applied: number;
  outline: number;
  unclear: number;
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
  onUrlChange(url: string): void;
  onLimitChange(limit: number): void;
}

const POS_KEY = "hh_panel_pos";

export class Panel implements LogSink {
  private root = document.createElement("div");

  constructor(
    private storage: KeyValueStorage,
    private handlers: PanelHandlers,
    private initial: { url: string; limit: number },
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
        <div class="hh-stats">
          ${stat("applied", "Отклики")}${stat("outline", "Аутлайн")}${stat("unclear", "Непонятное")}
          ${stat("total", "Всего")}${stat("daily", "За день")}
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
          <button class="hh-btn hh-btn-warn" id="hh-btn-outline">Аутлайн</button>
          <button class="hh-btn hh-btn-warn" id="hh-btn-unclear">Непонятное</button>
          <button class="hh-btn" id="hh-btn-clear">Очистить</button>
        </div>
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

  setStep(text: string): void {
    this.el("hh-step-desc").textContent = text;
  }

  setNextEnabled(enabled: boolean): void {
    this.el<HTMLButtonElement>("hh-btn-next").disabled = !enabled;
  }

  setAutoActive(active: boolean): void {
    const btn = this.el("hh-btn-auto");
    btn.textContent = active ? "Стоп" : "Авто";
    btn.classList.toggle("hh-btn-primary", active);
  }

  setUrl(url: string): void {
    this.el<HTMLInputElement>("hh-input-url").value = url;
  }

  setStats(s: PanelStats): void {
    const set = (id: string, value: string | number) => (this.el(`hh-stat-${id}`).textContent = String(value));
    set("applied", s.applied);
    set("outline", s.outline);
    set("unclear", s.unclear);
    set("total", s.total || "-");
    set("daily", `${s.daily} / ${s.dailyLimit}`);
  }
}
