import { describe, expect, it, vi } from "vitest";
import { BackendClient, type ApiSender } from "../src/api/client";
import { executeApiRequest, isApiRequest, type ApiResponse } from "../src/api/transport";
import { DEFAULT_SERVER, loadConfig } from "../src/core/config";
import { MemoryStorage } from "../src/infra/storage";

describe("server config", () => {
  it("дополняет сохранённый конфиг настройками сервера по умолчанию", async () => {
    const storage = new MemoryStorage();
    await storage.set("hh_autoapply_config", { dailyLimit: 10 });
    expect((await loadConfig(storage)).server).toEqual(DEFAULT_SERVER);

    await storage.set("hh_autoapply_config", { server: { apiKey: "k" } });
    expect((await loadConfig(storage)).server).toEqual({ url: DEFAULT_SERVER.url, apiKey: "k" });
  });
});

describe("executeApiRequest", () => {
  const ok = (body = "{}") => vi.fn(async () => new Response(body, { status: 200 }));

  it("склеивает адрес, ставит ключ и JSON-тело", async () => {
    const fetchFn = ok("[]");
    const res = await executeApiRequest(
      { type: "hh-api", path: "/api/import", method: "POST", body: { a: 1 } },
      { url: "http://127.0.0.1:8080/", apiKey: "secret" },
      fetchFn as unknown as typeof fetch,
    );
    expect(res).toEqual({ status: 200, body: "[]" });
    const [url, init] = fetchFn.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("http://127.0.0.1:8080/api/import");
    expect(init.method).toBe("POST");
    expect(init.headers).toEqual({ "X-Api-Key": "secret", "Content-Type": "application/json" });
    expect(init.body).toBe('{"a":1}');
  });

  it("без ключа не шлёт заголовок, ошибку сети превращает в status 0", async () => {
    const fetchFn = vi.fn(async () => {
      throw new TypeError("Failed to fetch");
    });
    const res = await executeApiRequest({ type: "hh-api", path: "/health" }, { url: "http://x", apiKey: "" }, fetchFn);
    expect(res).toEqual({ status: 0, body: "", error: "Failed to fetch" });
  });

  it("отклоняет адрес без схемы, не делая запрос", async () => {
    const fetchFn = ok();
    const res = await executeApiRequest({ type: "hh-api", path: "/health" }, { url: "127.0.0.1:8080", apiKey: "" }, fetchFn as unknown as typeof fetch);
    expect(res.status).toBe(0);
    expect(fetchFn).not.toHaveBeenCalled();
  });

  it("принимает только свои сообщения с относительным путём", () => {
    expect(isApiRequest({ type: "hh-api", path: "/health" })).toBe(true);
    expect(isApiRequest({ type: "hh-api", path: "http://evil/" })).toBe(false);
    expect(isApiRequest({ type: "other", path: "/health" })).toBe(false);
    expect(isApiRequest(null)).toBe(false);
  });
});

describe("BackendClient.check", () => {
  const sender = (responses: Record<string, ApiResponse>): ApiSender => async (req) => responses[req.path];

  it("сообщает о связи и размере очереди", async () => {
    const client = new BackendClient(sender({
      "/health": { status: 200, body: '{"status":"ok"}' },
      "/api/stats": { status: 200, body: '{"Queued":5,"Skipped":2}' },
    }));
    expect(await client.check()).toEqual({ ok: true, message: "Связь есть: вакансий на сервере 7, в очереди 5" });
  });

  it("различает недоступный сервер и неверный ключ", async () => {
    const down = new BackendClient(sender({ "/health": { status: 0, body: "", error: "timeout" } }));
    expect(await down.check()).toEqual({ ok: false, message: "Сервер недоступен: timeout" });

    const badKey = new BackendClient(sender({
      "/health": { status: 200, body: "{}" },
      "/api/stats": { status: 401, body: "" },
    }));
    expect(await badKey.check()).toEqual({ ok: false, message: "Сервер доступен, но токен неверный — выпустите новый (POST /api/tokens)" });
  });

  it("переживает падение канала к фону", async () => {
    const client = new BackendClient(async () => {
      throw new Error("Extension context invalidated.");
    });
    expect(await client.check()).toEqual({ ok: false, message: "Сервер недоступен: Extension context invalidated." });
  });
});
