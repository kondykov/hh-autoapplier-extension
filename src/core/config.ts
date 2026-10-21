import { DEFAULT_RESUMES, type ResumeProfile } from "../resume/profiles";
import { EMPTY_RULES, type FilterRules } from "../filter/rules";
import type { KeyValueStorage } from "../infra/storage";

export interface SearchPreset {
  name: string;
  url: string;
  letter?: string;
  rules?: FilterRules;
}

export interface AppConfig {
  coverLetterTemplate: string;
  searchRedirectUrl: string;
  dailyLimit: number;
  presets: SearchPreset[];
  rules: FilterRules;
  resumes: ResumeProfile[];
}

declare const __DEFAULT_LETTER__: string | undefined;
const LOCAL_LETTER = typeof __DEFAULT_LETTER__ === "string" ? __DEFAULT_LETTER__ : "";

// Личные данные (имя, контакты) задаются пользователем в настройках, а не хранятся в коде
export const DEFAULT_CONFIG: AppConfig = {
  coverLetterTemplate: LOCAL_LETTER || "Здравствуйте.\nМеня заинтересовала ваша вакансия.\n\nС уважением,",
  searchRedirectUrl: "https://hh.ru/search/vacancy?text=QA",
  dailyLimit: 150,
  presets: [],
  rules: EMPTY_RULES,
  resumes: DEFAULT_RESUMES,
};

const KEY = "hh_autoapply_config";

export async function loadConfig(storage: KeyValueStorage): Promise<AppConfig> {
  const saved = await storage.get<Partial<AppConfig>>(KEY, {});
  return { ...DEFAULT_CONFIG, ...saved, rules: { ...EMPTY_RULES, ...saved.rules } };
}

export function saveConfig(storage: KeyValueStorage, config: AppConfig): Promise<void> {
  return storage.set(KEY, config);
}

