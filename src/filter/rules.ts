import { extractSalary } from "../report/extractors";

export interface FilterRules {
  stopWords: string[];
  requiredStack: string[]; // достаточно любого из списка
  preferredStack: string[];
  minSalary: number; // 0 — не проверять
  formats: string[]; // пусто — любой
  minScore: number; // 0 — не проверять
}

export const EMPTY_RULES: FilterRules = {
  stopWords: [],
  requiredStack: [],
  preferredStack: [],
  minSalary: 0,
  formats: [],
  minScore: 0,
};

export interface FilterTarget {
  title: string;
  text: string; // название + сниппеты карточки
  stack: string[];
  salary: string;
  format: string;
}

export interface FilterVerdict {
  pass: boolean;
  score: number;
  reasons: string[];
}

export const parseList = (raw: string): string[] =>
  raw
    .split(/[,;\n]/)
    .map((s) => s.trim())
    .filter(Boolean);

// Максимальная сумма в рублях; для других валют и отсутствия оплаты — null (не отсекаем)
export function salaryRub(salary: string): number | null {
  const s = extractSalary(salary);
  if (!s || !/₽|руб/i.test(s)) return null;
  const nums = (s.match(/\d[\d\s]*/g) ?? []).map((n) => parseInt(n.replace(/\s/g, ""), 10)).filter((n) => n > 0);
  return nums.length ? Math.max(...nums) : null;
}

const norm = (s: string) => s.toLowerCase().replace(/ё/g, "е");
const has = (haystack: string, term: string) => haystack.includes(norm(term));

export function evaluate(target: FilterTarget, rules: FilterRules): FilterVerdict {
  const haystack = norm(`${target.title} ${target.text} ${target.stack.join(" ")}`);
  const reasons: string[] = [];
  let score = 0;

  const stop = rules.stopWords.find((w) => has(haystack, w));
  if (stop) reasons.push(`стоп-слово «${stop}»`);

  const requiredHits = rules.requiredStack.filter((t) => has(haystack, t));
  if (rules.requiredStack.length && !requiredHits.length) reasons.push("нет обязательных технологий");
  score += requiredHits.length * 2;
  score += rules.preferredStack.filter((t) => has(haystack, t)).length;

  const rub = salaryRub(target.salary);
  if (rules.minSalary > 0 && rub !== null) {
    if (rub < rules.minSalary) reasons.push(`оплата ${rub} < ${rules.minSalary}`);
    else score += 1;
  }

  if (rules.formats.length && target.format) {
    const f = norm(target.format);
    if (!rules.formats.some((x) => f.includes(norm(x).slice(0, 5)))) reasons.push(`формат «${target.format}»`);
  }

  if (rules.minScore > 0 && score < rules.minScore) reasons.push(`баллы ${score} < ${rules.minScore}`);
  return { pass: reasons.length === 0, score, reasons };
}
