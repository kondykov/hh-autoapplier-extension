import { normalizeText } from "../dom/query";

export interface ResumeProfile {
  name: string;
  // Подстрока названия резюме на hh.ru, по которой оно находится в списке
  match: string;
  keywords: string[];
  // Своё письмо для этого резюме; пусто — используется универсальное
  letter?: string;
}

export const DEFAULT_RESUMES: ResumeProfile[] = [
  {
    name: ".NET",
    match: "C# .NET",
    keywords: ["c#", ".net", "asp.net", "dotnet", "entity framework", "ef core", "blazor", "wpf"],
  },
  {
    name: "PHP",
    match: "PHP",
    keywords: ["php", "laravel", "symfony", "yii", "bitrix", "битрикс", "nuxt", "wordpress"],
  },
];

const norm = (s: string) => normalizeText(s).toLowerCase().replace(/ё/g, "е");

// Побеждает профиль с наибольшим числом совпавших ключевых слов; при нуле или ничьей между лидерами — null
export function pickProfile(profiles: ResumeProfile[], haystack: string): ResumeProfile | null {
  const text = norm(haystack);
  const scored = profiles
    .map((p) => ({ p, score: p.keywords.filter((k) => text.includes(norm(k))).length }))
    .filter((s) => s.score > 0)
    .sort((a, b) => b.score - a.score);
  if (!scored.length) return null;
  if (scored.length > 1 && scored[0].score === scored[1].score) return null;
  return scored[0].p;
}
