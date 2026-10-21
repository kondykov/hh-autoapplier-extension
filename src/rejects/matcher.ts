import { normalizeText } from "../dom/query";

export const REJECT_KEYWORDS = [
  "отказ",
  "отказали",
  "к сожалению",
  "не готовы",
  "не подходит",
  "другого кандидата",
  "других кандидатов",
  "свяжемся с вами",
  "кадровый резерв",
  "кадровом резерве",
  "спасибо за отклик",
  "вакансия закрыта",
  "вакансия неактуальна",
  "не сможем предложить",
  "не будем рассматривать",
];

export function isRejection(text: string, keywords: string[] = REJECT_KEYWORDS): boolean {
  const t = normalizeText(text).toLowerCase();
  return keywords.some((k) => t.includes(k));
}
