import { SELECTORS } from "../core/selectors";
import { extractStack, extractWorkFormat } from "../report/extractors";
import { isAlreadyResponded, normalizeText, qs, qsa } from "../dom/query";

// Карточка выдачи для POST /api/import. Отбор и оценка — на backend (#27): здесь только то, что видно на странице.
// Зарплату и описание целиком сервер берёт со страницы вакансии, когда подтверждает импортную карточку
export interface ImportCard {
  id: string;
  title: string;
  company: string;
  url: string;
  description: string;
  workFormat: string;
  stack: string[];
}

const vacancyId = (href: string) => href.match(/\/vacancy\/(\d+)/)?.[1];

// Карточки текущей страницы выдачи; уже с откликом и без ссылки на вакансию — пропускаются
export function collectCards(root: ParentNode = document): ImportCard[] {
  const cards: ImportCard[] = [];
  for (const card of qsa(SELECTORS.vacancyCard, root)) {
    if (isAlreadyResponded(card)) continue;

    const titleEl = qs<HTMLAnchorElement>(SELECTORS.vacancyTitleLink, card) ?? qs<HTMLAnchorElement>(SELECTORS.vacancyTitle, card);
    const id = vacancyId(titleEl?.href ?? "");
    const title = normalizeText(titleEl?.textContent);
    if (!id || !title) continue;

    const labels = qsa(SELECTORS.cardFormatLabels, card).map((e) => e.textContent).join(" ");
    const snippets = normalizeText(qsa(SELECTORS.snippets, card).map((e) => e.textContent).join(" "));
    cards.push({
      id,
      title,
      company: normalizeText(qs(SELECTORS.companyName, card)?.textContent),
      url: `https://hh.ru/vacancy/${id}`,
      description: snippets,
      workFormat: extractWorkFormat(`${labels} ${card.textContent}`),
      stack: extractStack(title, snippets),
    });
  }
  return cards;
}
