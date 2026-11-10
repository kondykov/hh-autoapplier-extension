// Контракт API с backend (#77): растёт при несовместимом изменении. Тот же номер — ApiContract.Version на backend,
// совпадение проверяет tests/api-version.test.ts
export const API_VERSION = 1;

export interface ServerVersion {
  version: string;
  commit: string;
  apiVersion: number;
}

// unknown — сервер ответил 404: сборка старше #77, совместимость проверить нечем
export type ServerVersionResult =
  | { state: "compatible"; server: ServerVersion }
  | { state: "incompatible"; server: ServerVersion }
  | { state: "unknown" }
  | { state: "error"; error: string };

export const describeServer = (server: ServerVersion): string =>
  `${server.version} (${server.commit}), контракт API ${server.apiVersion}`;

export const incompatibleMessage = (server: ServerVersion): string =>
  `Сервер несовместим с расширением: контракт API ${server.apiVersion}, нужен ${API_VERSION} (сервер ${server.version}, ${server.commit})`;

export const UNKNOWN_VERSION_MESSAGE = "Версия сервера неизвестна: сборка backend старше 0.1.0, совместимость не проверить";
