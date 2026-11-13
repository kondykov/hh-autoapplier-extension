/// <reference types="vite/client" />
import { describe, expect, it } from "vitest";
import source from "../server/src/HhAutoApplier.Application/ApiContract.cs?raw";
import { BackendClient, type ApiSender } from "../src/api/client";
import type { ApiResponse } from "../src/api/transport";
import { API_VERSION } from "../src/api/version";

const sender = (responses: Record<string, ApiResponse>): ApiSender => async (req) => responses[req.path];
const version = (apiVersion: number): ApiResponse => ({ status: 200, body: JSON.stringify({ version: "0.1.0", commit: "abc1234", apiVersion }) });
const reachable = { "/health": { status: 200, body: "{}" }, "/api/stats": { status: 200, body: JSON.stringify({ Queued: 5, Applied: 2 }) } };

describe("контракт API (#77)", () => {
  it("номер в расширении совпадает с backend", () => {
    const backend = Number(source.match(/const int Version = (\d+);/)?.[1]);

    expect(backend).toBe(API_VERSION);
  });
});

describe("BackendClient.getServerVersion (#77)", () => {
  it("тот же контракт — совместим", async () => {
    const result = await new BackendClient(sender({ "/api/version": version(API_VERSION) })).getServerVersion();
    expect(result).toEqual({ state: "compatible", server: { version: "0.1.0", commit: "abc1234", apiVersion: API_VERSION } });
  });

  it("другой контракт — несовместим", async () => {
    const result = await new BackendClient(sender({ "/api/version": version(API_VERSION + 1) })).getServerVersion();
    expect(result.state).toBe("incompatible");
  });

  it("404 — сборка без версии", async () => {
    expect(await new BackendClient(sender({ "/api/version": { status: 404, body: "" } })).getServerVersion()).toEqual({ state: "unknown" });
  });

  it("сбой, не JSON или ответ без контракта — ошибка, а не совместимость", async () => {
    const down = await new BackendClient(sender({ "/api/version": { status: 500, body: "сбой" } })).getServerVersion();
    const html = await new BackendClient(sender({ "/api/version": { status: 200, body: "<html>" } })).getServerVersion();
    const empty = await new BackendClient(sender({ "/api/version": { status: 200, body: "{}" } })).getServerVersion();

    expect([down.state, html.state, empty.state]).toEqual(["error", "error", "error"]);
  });
});

describe("BackendClient.check: версия сервера (#77)", () => {
  it("показывает версию и коммит сервера", async () => {
    const result = await new BackendClient(sender({ ...reachable, "/api/version": version(API_VERSION) })).check();
    expect(result).toEqual({ ok: true, message: `Связь есть: вакансий на сервере 7, в очереди 5. Сервер 0.1.0 (abc1234), контракт API ${API_VERSION}` });
  });

  it("старая сборка: связь есть, но с предупреждением", async () => {
    const result = await new BackendClient(sender({ ...reachable, "/api/version": { status: 404, body: "" } })).check();
    expect(result.ok).toBe(true);
    expect(result.message).toContain("Версия сервера неизвестна");
  });

  it("несовместимый сервер — проверка не проходит", async () => {
    const result = await new BackendClient(sender({ ...reachable, "/api/version": version(API_VERSION + 1) })).check();
    expect(result.ok).toBe(false);
    expect(result.message).toContain("несовместим");
  });
});
