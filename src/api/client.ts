import type { ApiRequest, ApiResponse } from "./transport";

export type ApiSender = (request: ApiRequest) => Promise<ApiResponse>;

// В расширении запрос уходит в фоновый service worker
export const runtimeSender: ApiSender = (request) => chrome.runtime.sendMessage<ApiRequest, ApiResponse>(request);

export interface CheckResult {
  ok: boolean;
  message: string;
}

// Клиент backend для content script. Методы очереди и статусов появятся с режимом «из очереди» (#15)
export class BackendClient {
  constructor(private send: ApiSender) {}

  // /health открыт без ключа — проверяет адрес; /api/stats требует ключ — проверяет ключ
  async check(): Promise<CheckResult> {
    const health = await this.call({ type: "hh-api", path: "/health" });
    if (health.status === 0) return { ok: false, message: `Сервер недоступен: ${health.error ?? "нет ответа"}` };
    if (health.status !== 200) return { ok: false, message: `Сервер ответил ${health.status} на /health — это точно backend?` };

    const stats = await this.call({ type: "hh-api", path: "/api/stats" });
    if (stats.status === 401) return { ok: false, message: "Сервер доступен, но ключ API неверный" };
    if (stats.status !== 200) return { ok: false, message: `Сервер доступен, но /api/stats ответил ${stats.status}` };

    const counts = JSON.parse(stats.body) as Record<string, number>;
    const total = Object.values(counts).reduce((sum, n) => sum + n, 0);
    return { ok: true, message: `Связь есть: вакансий на сервере ${total}, в очереди ${counts.Queued ?? 0}` };
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
