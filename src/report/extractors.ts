import { normalizeText } from "../dom/query";

// Словарь технологий; поиск по границам слов без учёта регистра
const STACK_TERMS: [string, RegExp][] = [
  ["C#", /(?<![\w#])c#(?!\w)/i],
  [".NET", /(?<!\w)\.net\b|\bdotnet\b/i],
  ["ASP.NET", /\basp\.net\b/i],
  ["Entity Framework", /\bentity framework\b|\bef core\b/i],
  ["PHP", /\bphp\b/i],
  ["Laravel", /\blaravel\b/i],
  ["Symfony", /\bsymfony\b/i],
  ["Python", /\bpython\b/i],
  ["Django", /\bdjango\b/i],
  ["FastAPI", /\bfastapi\b/i],
  ["Java", /\bjava\b(?!\s*script)/i],
  ["Kotlin", /\bkotlin\b/i],
  ["Go", /\bgolang\b/i],
  ["JavaScript", /\bjavascript\b|\bjs\b/i],
  ["TypeScript", /\btypescript\b|\bts\b/i],
  ["React", /\breact\b/i],
  ["Vue", /\bvue(\.js)?\b/i],
  ["Angular", /\bangular\b/i],
  ["Node.js", /\bnode(\.js)?\b/i],
  ["SQL", /\bsql\b/i],
  ["PostgreSQL", /\bpostgres(ql)?\b/i],
  ["MySQL", /\bmysql\b/i],
  ["MS SQL", /\bms\s?sql\b|\bt-sql\b/i],
  ["MongoDB", /\bmongo(db)?\b/i],
  ["Redis", /\bredis\b/i],
  ["RabbitMQ", /\brabbitmq\b/i],
  ["Kafka", /\bkafka\b/i],
  ["Docker", /\bdocker\b/i],
  ["Kubernetes", /\bkubernetes\b|\bk8s\b/i],
  ["CI/CD", /\bci\s?\/\s?cd\b/i],
  ["Git", /\bgit\b/i],
  ["REST", /\brest(ful)?\b/i],
  ["gRPC", /\bgrpc\b/i],
  ["Linux", /\blinux\b/i],
  ["Selenium", /\bselenium\b/i],
  ["Playwright", /\bplaywright\b/i],
  ["Postman", /\bpostman\b/i],
  ["Azure", /\bazure\b/i],
  ["AWS", /\baws\b/i],
];

export function extractStack(...texts: (string | null | undefined)[]): string[] {
  const text = normalizeText(texts.join(" "));
  const found = STACK_TERMS.filter(([, re]) => re.test(text)).map(([name]) => name);
  // ASP.NET уже подразумевает .NET
  return found.includes("ASP.NET") ? found.filter((n) => n !== ".NET") : found;
}

const SALARY_RE =
  /(?:(?:от|до)\s+)?\d[\d\s]*(?:[–-]\s*\d[\d\s]*)?\s*(?:₽|руб\.?|\$|€|USD|EUR|KZT|₸)(?:\s*(?:на руки|до вычета налогов))?/i;

export function extractSalary(text: string | null | undefined): string {
  return normalizeText(SALARY_RE.exec(normalizeText(text))?.[0]);
}

const FORMAT_RE = /удалённ\S*|удаленн\S*|гибрид\S*|на месте работодателя|офис|разъездн\S*|вахта/i;

export function extractWorkFormat(text: string | null | undefined): string {
  return normalizeText(FORMAT_RE.exec(normalizeText(text))?.[0]);
}
