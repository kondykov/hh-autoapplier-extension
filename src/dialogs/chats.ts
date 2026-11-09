// Чаты откликов hh.ru через API страницы «Чаты» (chatik.hh.ru, проверено вживую 2026-10-26).
// Только GET: список и данные чата не помечают сообщения прочитанными (это делает отдельный POST при открытии чата)
export const CHATIK = "https://chatik.hh.ru/chatik/api";

// Чат для POST /api/dialogs: последнее сообщение и состояние отклика
export interface DialogChat {
  chatId: number;
  vacancyId: string | null;
  title: string;
  company: string;
  // applicantState: DISCARD, INVITATION, INTERVIEW, RESPONSE…; null — не сообщено
  state: string | null;
  messageId: number;
  text: string;
  at: string;
  fromEmployer: boolean;
}

export interface ChatsPage {
  chats: DialogChat[];
  // Участник-соискатель: нужен для chat_data
  applicantId: string | null;
  nextFrom: string | null;
  // Самое старое событие страницы (мс): по нему сбор останавливается
  oldest: number | null;
}

export interface WritePossibility {
  canWrite: boolean;
  // Почему нельзя: WITHOUT_INVITATION, DISABLED_BY_EMPLOYER…
  reason: string | null;
}

interface RawMessage {
  id?: number;
  creationTime?: string;
  text?: string;
  participantId?: string;
  workflowTransition?: { applicantState?: string | null } | null;
}

interface RawChat {
  id?: number;
  type?: string;
  resources?: { VACANCY?: string[] };
  currentParticipantId?: string;
  lastMessage?: RawMessage | null;
  lastActivityTime?: string;
}

interface RawChats {
  chats?: { items?: RawChat[]; nextFrom?: string | null };
  chatsDisplayInfo?: Record<string, { title?: string; subtitle?: string }>;
}

export function parseChats(json: unknown): ChatsPage {
  const raw = (json ?? {}) as RawChats;
  const items = raw.chats?.items ?? [];
  const display = raw.chatsDisplayInfo ?? {};
  const chats: DialogChat[] = [];
  for (const item of items) {
    const message = item.lastMessage;
    if (typeof item.id !== "number" || item.type !== "NEGOTIATION" || !message || typeof message.id !== "number") continue;
    const info = display[String(item.id)] ?? {};
    chats.push({
      chatId: item.id,
      vacancyId: item.resources?.VACANCY?.[0] ?? null,
      title: info.title ?? "",
      company: info.subtitle ?? "",
      state: message.workflowTransition?.applicantState ?? null,
      messageId: message.id,
      text: message.text ?? "",
      at: message.creationTime ?? item.lastActivityTime ?? new Date(0).toISOString(),
      fromEmployer: message.participantId !== item.currentParticipantId,
    });
  }
  // Время со смещением (+03:00): сравнивать числами, не строками
  const times = items.map((i) => Date.parse(i.lastActivityTime ?? i.lastMessage?.creationTime ?? "")).filter((t) => !Number.isNaN(t));
  return {
    chats,
    applicantId: items.find((i) => i.currentParticipantId)?.currentParticipantId ?? null,
    nextFrom: raw.chats?.nextFrom ?? null,
    oldest: times.length ? Math.min(...times) : null,
  };
}

export function parseWritePossibility(json: unknown): WritePossibility {
  const wp = (json as { chat?: { writePossibility?: { name?: string; writeDisabledReasons?: string[] } } } | null)?.chat?.writePossibility;
  const name = wp?.name ?? "";
  if (name.startsWith("ENABLED")) return { canWrite: true, reason: null };
  return { canWrite: false, reason: wp?.writeDisabledReasons?.[0] ?? (name || "нет данных о переписке") };
}

const REASONS: Record<string, string> = {
  WITHOUT_INVITATION: "работодатель отклонил отклик — переписка только после приглашения",
  DISABLED_BY_EMPLOYER: "работодатель закрыл переписку",
};

export const describeWriteReason = (reason: string | null): string => (reason && REASONS[reason]) || reason || "писать нельзя";

export type JsonFetch = (url: string) => Promise<unknown>;

// В расширении — fetch со страницы hh.ru с её cookie
export const pageFetch: JsonFetch = async (url) => {
  const res = await fetch(url, { credentials: "include" });
  if (!res.ok) throw new Error(`hh.ru ответил ${res.status}`);
  return res.json();
};

export class ChatSource {
  private applicantId: string | null = null;

  constructor(
    private get: JsonFetch = pageFetch,
    private pause: () => Promise<void> = async () => {},
  ) {}

  // Последние чаты: не больше maxPages страниц по 20 и не старше maxAgeDays
  async recent(maxPages = 3, maxAgeDays = 30, now = Date.now()): Promise<DialogChat[]> {
    const since = now - maxAgeDays * 24 * 3600 * 1000;
    const result: DialogChat[] = [];
    let from: string | null = null;
    for (let i = 0; i < maxPages; i++) {
      const page = parseChats(await this.get(`${CHATIK}/chats?do_not_track_session_events=true${from ? `&from=${encodeURIComponent(from)}` : ""}`));
      this.applicantId ??= page.applicantId;
      result.push(...page.chats.filter((c) => Date.parse(c.at) >= since));
      from = page.nextFrom;
      if (!from || (page.oldest && page.oldest < since)) break;
      await this.pause();
    }
    return result;
  }

  async writePossibility(chatId: number): Promise<WritePossibility> {
    const applicant = this.applicantId ? `&applicantId=${encodeURIComponent(this.applicantId)}` : "";
    return parseWritePossibility(await this.get(`${CHATIK}/chat_data?chatId=${chatId}${applicant}&do_not_track_session_events=true`));
  }
}
