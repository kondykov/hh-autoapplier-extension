import type { KeyValueStorage } from "../infra/storage";

export interface SearchPreset {
  name: string;
  url: string;
}

export interface AppConfig {
  coverLetterTemplate: string;
  searchRedirectUrl: string;
  dailyLimit: number;
  presets: SearchPreset[];
}

// Личные данные (имя, контакты) задаются пользователем в настройках, а не хранятся в коде
export const DEFAULT_CONFIG: AppConfig = {
  coverLetterTemplate: "Здравствуйте.\nМеня заинтересовала ваша вакансия.\n\nС уважением,",
  searchRedirectUrl: "https://hh.ru/search/vacancy?text=QA",
  dailyLimit: 150,
  presets: [],
};

const KEY = "hh_autoapply_config";

export async function loadConfig(storage: KeyValueStorage): Promise<AppConfig> {
  const saved = await storage.get<Partial<AppConfig>>(KEY, {});
  return { ...DEFAULT_CONFIG, ...saved };
}

export function saveConfig(storage: KeyValueStorage, config: AppConfig): Promise<void> {
  return storage.set(KEY, config);
}
