import type { KeyValueStorage } from "../infra/storage";

// Backend: адрес и ключ X-Api-Key. Запросы к нему делает фоновый service worker расширения
export interface ServerSettings {
  url: string;
  apiKey: string;
}

export const DEFAULT_SERVER: ServerSettings = { url: "http://127.0.0.1:8080", apiKey: "" };

// Отбор вакансий, резюме и письма под вакансию — на backend (#27); здесь только исполнение и запасные значения
export interface AppConfig {
  // Письмо, если backend не прислал своего (LLM выключена или не успела)
  coverLetterTemplate: string;
  dailyLimit: number;
  // Откликов за один запуск очереди: после стольких отправленных очередь останавливается сама; 0 — без ограничения
  queueRunLimit: number;
  server: ServerSettings;
  // Поиск на сервере, в который уходит выдача со страницы (POST /api/import)
  importSearch: string;
  // Режим «из очереди» без нажатия «Отправить»: по умолчанию включён, выключается только явно в панели
  queueDryRun: boolean;
  // Автоответ на новый отказ работодателя (решение владельца 2026-10-26); отправляется, только где hh.ru разрешает писать
  rejectionReply: string;
}

declare const __DEFAULT_LETTER__: string | undefined;
const LOCAL_LETTER = typeof __DEFAULT_LETTER__ === "string" ? __DEFAULT_LETTER__ : "";

// Личные данные (имя, контакты) задаются пользователем в настройках, а не хранятся в коде
export const DEFAULT_CONFIG: AppConfig = {
  coverLetterTemplate: LOCAL_LETTER || "Здравствуйте.\nМеня заинтересовала ваша вакансия.\n\nС уважением,",
  dailyLimit: 150,
  queueRunLimit: 0,
  server: DEFAULT_SERVER,
  importSearch: "",
  queueDryRun: true,
  rejectionReply: "Спасибо за ответ и удачи в поиске!",
};

const KEY = "hh_autoapply_config";

// Только известные поля: настройки прежнего локального отбора (presets, rules, resumes, searchRedirectUrl) отбрасываются
export async function loadConfig(storage: KeyValueStorage): Promise<AppConfig> {
  const saved = await storage.get<Partial<AppConfig>>(KEY, {});
  return {
    coverLetterTemplate: saved.coverLetterTemplate ?? DEFAULT_CONFIG.coverLetterTemplate,
    dailyLimit: saved.dailyLimit ?? DEFAULT_CONFIG.dailyLimit,
    queueRunLimit: saved.queueRunLimit ?? DEFAULT_CONFIG.queueRunLimit,
    server: { ...DEFAULT_SERVER, ...saved.server },
    importSearch: saved.importSearch ?? DEFAULT_CONFIG.importSearch,
    queueDryRun: saved.queueDryRun ?? DEFAULT_CONFIG.queueDryRun,
    rejectionReply: saved.rejectionReply ?? DEFAULT_CONFIG.rejectionReply,
  };
}

export function saveConfig(storage: KeyValueStorage, config: AppConfig): Promise<void> {
  return storage.set(KEY, config);
}
