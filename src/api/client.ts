import type { DialogChat } from "../dialogs/chats";
import type { QuestionAnswer, ServerQuestion } from "../questions/fill";
import type { ImportCard } from "../search/vacancy-collector";
import type { QueueItem, StatusReport } from "../queue/queue-mode";
import type { ApiRequest, ApiResponse } from "./transport";

export type ApiSender = (request: ApiRequest) => Promise<ApiResponse>;

// В расширении запрос уходит в фоновый service worker
export const runtimeSender: ApiSender = (request) => chrome.runtime.sendMessage<ApiRequest, ApiResponse>(request);

export interface CheckResult {
  ok: boolean;
  message: string;
}

export type QueueResult = { ok: true; items: QueueItem[] } | { ok: false; error: string };

export type ReportResult = { ok: true } | { ok: false; error: string };

export type ExtensionEvent = "Captcha" | "DailyLimit";

// Ответ импорта: waiting — карточки ждут подтверждения сбором сервера
export interface ImportCounts {
  received: number;
  added: number;
  queued: number;
  skipped: number;
  waiting: number;
}

export type ImportResult = { ok: true; result: ImportCounts } | { ok: false; error: string };

export interface ResumeMatch {
  name: string;
  match: string;
}

// Чат, где ждёт автоответ на отказ
export interface AutoReply {
  chatId: number;
  vacancyId: string | null;
  title: string;
  company: string;
}

export interface DialogsCounts {
  received: number;
  newMessages: number;
  rejected: number;
  invited: number;
  // Отклики, чей статус потерялся, найденные по чатам: на сервере стали Applied
  recovered?: number;
  autoReplies: AutoReply[];
}

export type DialogsResult = { ok: true; result: DialogsCounts } | { ok: false; error: string };

export type ResumesResult = { ok: true; resumes: ResumeMatch[] } | { ok: false; error: string };

interface VacancyDto {
  id: string;
  title: string;
  company: string;
  resumeProfile: string | null;
  coverLetter?: string | null;
  questions?: { question: string; answer: string[] }[];
}

// Клиент backend для content script
export class BackendClient {
  constructor(private send: ApiSender) {}

  // /health открыт без токена — проверяет адрес; /api/stats требует токен — проверяет токен
  async check(): Promise<CheckResult> {
    const health = await this.call({ type: "hh-api", path: "/health" });
    if (health.status === 0) return { ok: false, message: `Сервер недоступен: ${health.error ?? "нет ответа"}` };
    if (health.status !== 200) return { ok: false, message: `Сервер ответил ${health.status} на /health — это точно backend?` };

    const stats = await this.call({ type: "hh-api", path: "/api/stats" });
    if (stats.status === 401) return { ok: false, message: "Сервер доступен, но токен неверный — выпустите новый (POST /api/tokens)" };
    if (stats.status !== 200) return { ok: false, message: `Сервер доступен, но /api/stats ответил ${stats.status}` };

    const counts = JSON.parse(stats.body) as Record<string, number>;
    const total = Object.values(counts).reduce((sum, n) => sum + n, 0);
    return { ok: true, message: `Связь есть: вакансий на сервере ${total}, в очереди ${counts.Queued ?? 0}` };
  }

  // Лучшие одобренные вакансии; берём с запасом, чтобы пропускать уже опробованные в пробном режиме
  async getQueue(limit = 20): Promise<QueueResult> {
    const res = await this.call({ type: "hh-api", path: `/api/queue?limit=${limit}` });
    if (res.status !== 200) return { ok: false, error: describe(res) };

    const items = (JSON.parse(res.body) as VacancyDto[]).map((v) => ({
      id: v.id,
      title: v.title,
      company: v.company,
      resume: v.resumeProfile,
      letter: v.coverLetter ?? undefined,
      answers: answered(v.questions),
    }));
    return { ok: true, items };
  }

  // Анкета работодателя: вопросы на сервер, ответы подготовит LLM, вакансия вернётся в очередь с ними
  async submitQuestionnaire(id: string, questions: ServerQuestion[]): Promise<ReportResult> {
    const res = await this.call({
      type: "hh-api",
      path: `/api/vacancies/${encodeURIComponent(id)}/questionnaire`,
      method: "POST",
      body: { questions },
    });
    return res.status === 200 ? { ok: true } : { ok: false, error: describe(res) };
  }

  async reportStatus(id: string, report: StatusReport, resume: string | null): Promise<ReportResult> {
    const res = await this.call({
      type: "hh-api",
      path: `/api/vacancies/${encodeURIComponent(id)}/status`,
      method: "POST",
      body: { status: report.status, reason: report.reason, resume },
    });
    return res.status === 200 ? { ok: true } : { ok: false, error: describe(res) };
  }

  // Выдача в свой поиск на сервере (#27): отбор и оценка — там. 400 — поиска с таким именем нет
  async importVacancies(search: string, vacancies: ImportCard[]): Promise<ImportResult> {
    const res = await this.call({ type: "hh-api", path: "/api/import", method: "POST", body: { search, vacancies } });
    if (res.status !== 200) return { ok: false, error: describe(res) };
    return { ok: true, result: JSON.parse(res.body) as ImportCounts };
  }

  // Профили резюме с сервера: по match резюме находится в списке на hh.ru
  async getResumes(): Promise<ResumesResult> {
    const res = await this.call({ type: "hh-api", path: "/api/resumes" });
    if (res.status !== 200) return { ok: false, error: describe(res) };
    return { ok: true, resumes: (JSON.parse(res.body) as ResumeMatch[]).map(({ name, match }) => ({ name, match })) };
  }

  // Чаты откликов (#26): статусы и сообщения работодателей; разбор LLM — на сервере, в ответе — чаты для автоответа на отказ
  async importDialogs(chats: DialogChat[]): Promise<DialogsResult> {
    const res = await this.call({ type: "hh-api", path: "/api/dialogs", method: "POST", body: { chats } });
    if (res.status !== 200) return { ok: false, error: describe(res) };
    return { ok: true, result: JSON.parse(res.body) as DialogsCounts };
  }

  async reportAutoReply(chatId: number, sent: boolean, error?: string): Promise<ReportResult> {
    const res = await this.call({ type: "hh-api", path: `/api/dialogs/${chatId}/auto-reply`, method: "POST", body: { sent, error } });
    return res.status === 200 ? { ok: true } : { ok: false, error: describe(res) };
  }

  // Событие для Telegram владельца (#28): сервер сам гасит повторы и молчит, если чат не привязан
  async reportEvent(kind: ExtensionEvent, message?: string): Promise<ReportResult> {
    const res = await this.call({ type: "hh-api", path: "/api/events", method: "POST", body: { kind, message } });
    return res.status === 200 ? { ok: true } : { ok: false, error: describe(res) };
  }

  private async call(request: ApiRequest): Promise<ApiResponse> {
    try {
      return await this.send(request);
    } catch (e) {
      // Фон не ответил: расширение перезагружено или service worker не поднялся
      return { status: 0, body: "", error: (e as Error).message };
    }
  }
}

// Ответы — только если отвечено на всё: неполную анкету конвейер всё равно не отправит
function answered(questions: VacancyDto["questions"]): QuestionAnswer[] | undefined {
  if (!questions?.length || questions.some((q) => q.answer.length === 0)) return undefined;
  return questions.map((q) => ({ question: q.question, answer: q.answer }));
}

function describe(res: ApiResponse): string {
  if (res.status === 0) return `сервер недоступен: ${res.error ?? "нет ответа"}`;
  if (res.status === 401) return "неверный токен";
  return `сервер ответил ${res.status}: ${res.body.slice(0, 200)}`;
}
