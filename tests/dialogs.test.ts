import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ChatSource, CHATIK, describeWriteReason, parseChats, parseWritePossibility } from "../src/dialogs/chats";
import { sendChatMessage } from "../src/dialogs/reply";
import { detectPageType } from "../src/dom/page-type";

const ME = "123";

// Ответ /chatik/api/chats в форме живого (2026-10-26), тексты выдуманы
function chatsJson(items: { id: number; vacancy?: string; state?: string | null; from?: string; at: string; type?: string }[], nextFrom: string | null = null) {
  return {
    chats: {
      items: items.map((i) => ({
        id: i.id,
        type: i.type ?? "NEGOTIATION",
        unreadCount: 0,
        resources: i.vacancy ? { VACANCY: [i.vacancy] } : {},
        currentParticipantId: ME,
        lastActivityTime: i.at,
        lastMessage: {
          id: i.id * 10,
          creationTime: i.at,
          text: `Сообщение ${i.id}`,
          participantId: i.from ?? "999",
          workflowTransition: i.state === undefined ? null : { applicantState: i.state },
        },
      })),
      found: items.length,
      nextFrom,
    },
    chatsDisplayInfo: Object.fromEntries(items.map((i) => [String(i.id), { title: `Вакансия ${i.id}`, subtitle: "Acme" }])),
  };
}

describe("parseChats", () => {
  it("последнее сообщение, вакансия, состояние и автор", () => {
    const page = parseChats(chatsJson([
      { id: 1, vacancy: "100", state: "DISCARD", at: "2026-10-26T11:09:13.037+03:00" },
      { id: 2, vacancy: "200", state: "RESPONSE", from: ME, at: "2026-10-25T03:03:30+03:00" },
      { id: 3, at: "2026-10-24T10:00:00+03:00", type: "SUPPORT" },
    ], "next"));

    expect(page.chats).toEqual([
      { chatId: 1, vacancyId: "100", title: "Вакансия 1", company: "Acme", state: "DISCARD", messageId: 10, text: "Сообщение 1", at: "2026-10-26T11:09:13.037+03:00", fromEmployer: true },
      expect.objectContaining({ chatId: 2, state: "RESPONSE", fromEmployer: false }),
    ]);
    expect(page.applicantId).toBe(ME);
    expect(page.nextFrom).toBe("next");
    expect(page.oldest).toBe(Date.parse("2026-10-24T10:00:00+03:00"));
  });

  it("мусор вместо ответа — пустая страница", () => {
    expect(parseChats(null).chats).toEqual([]);
    expect(parseChats({ chats: { items: [{ id: 1 }] } }).chats).toEqual([]);
  });
});

describe("parseWritePossibility", () => {
  it("ENABLED_* — можно писать, иначе причина", () => {
    expect(parseWritePossibility({ chat: { writePossibility: { name: "ENABLED_FOR_ALL", writeDisabledReasons: [] } } })).toEqual({ canWrite: true, reason: null });
    const closed = parseWritePossibility({ chat: { writePossibility: { name: "DISABLED_FOR_APPLICANT", writeDisabledReasons: ["WITHOUT_INVITATION"] } } });
    expect(closed).toEqual({ canWrite: false, reason: "WITHOUT_INVITATION" });
    expect(describeWriteReason(closed.reason)).toContain("после приглашения");
    expect(parseWritePossibility({}).canWrite).toBe(false);
  });
});

describe("ChatSource", () => {
  const NOW = Date.parse("2026-10-26T12:00:00Z");

  it("листает страницы до старых чатов; chat_data — с id соискателя", async () => {
    const get = vi.fn(async (url: string) => {
      if (url.includes("chat_data")) return { chat: { writePossibility: { name: "ENABLED_FOR_ALL" } } };
      if (!url.includes("from=")) return chatsJson([{ id: 1, at: "2026-10-26T10:00:00+03:00" }], "p2");
      return chatsJson([{ id: 2, at: "2026-10-20T10:00:00+03:00" }, { id: 3, at: "2026-08-01T10:00:00+03:00" }], "p3");
    });
    const source = new ChatSource(get);

    const chats = await source.recent(5, 30, NOW);

    // Чат 3 старше 30 дней; третья страница не запрашивается
    expect(chats.map((c) => c.chatId)).toEqual([1, 2]);
    expect(get).toHaveBeenCalledTimes(2);
    expect(get.mock.calls[1][0]).toBe(`${CHATIK}/chats?do_not_track_session_events=true&from=p2`);

    expect(await source.writePossibility(7)).toEqual({ canWrite: true, reason: null });
    expect(get.mock.calls[2][0]).toBe(`${CHATIK}/chat_data?chatId=7&applicantId=${ME}&do_not_track_session_events=true`);
  });

  it("не больше maxPages страниц", async () => {
    const get = vi.fn(async () => chatsJson([{ id: 1, at: "2026-10-26T10:00:00+03:00" }], "more"));
    await new ChatSource(get).recent(3, 30, NOW);
    expect(get).toHaveBeenCalledTimes(3);
  });
});

describe("страница чатов", () => {
  it("чаты и отклики — тип chat", () => {
    expect(detectPageType("https://tver.hh.ru/chat/123")).toBe("chat");
    expect(detectPageType("https://hh.ru/applicant/negotiations")).toBe("chat");
    expect(detectPageType("https://hh.ru/applicant/vacancy_response?vacancyId=1")).toBe("response");
  });
});

describe("sendChatMessage", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => {
    vi.useRealTimers();
    document.body.innerHTML = "";
  });

  const run = async <T>(promise: Promise<T>) => {
    await vi.runAllTimersAsync();
    return promise;
  };

  // Поле чата hh.ru (разметка с живой страницы 2026-10-26); отправка — по Enter, как у hh.ru без кнопки
  function renderInput(sends: boolean) {
    document.body.innerHTML = `<div data-qa="chatik-message-input"><textarea placeholder="Сообщение" data-qa="text-input"></textarea></div>`;
    const input = document.querySelector("textarea")!;
    const typed: string[] = [];
    input.addEventListener("input", () => typed.push(input.value));
    input.addEventListener("keydown", (e) => {
      if (sends && e.key === "Enter") input.value = "";
    });
    return typed;
  }

  it("вставляет текст и отправляет Enter; поле очистилось — ушло", async () => {
    const typed = renderInput(true);
    expect(await run(sendChatMessage("Спасибо!"))).toEqual({ ok: true });
    expect(typed).toEqual(["Спасибо!"]);
  });

  it("поле не очистилось — не ушло", async () => {
    renderInput(false);
    expect(await run(sendChatMessage("Спасибо!"))).toMatchObject({ ok: false, error: expect.stringContaining("не ушло") });
  });

  it("переписка закрыта — текст предупреждения, ничего не вводится", async () => {
    document.body.innerHTML = `<div data-qa="chatik-chat-warning">Работодатель отклонил отклик — переписка будет доступна после приглашения</div>`;
    expect(await run(sendChatMessage("Спасибо!"))).toEqual({ ok: false, error: "Работодатель отклонил отклик — переписка будет доступна после приглашения" });
  });
});
