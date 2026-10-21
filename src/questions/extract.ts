import { SELECTORS } from "../core/selectors";
import { normalizeText, qs, qsa } from "../dom/query";

export type QuestionType = "radio" | "checkbox" | "textarea" | "input" | "unknown";

export interface CollectedQuestion {
  question: string;
  type: QuestionType;
  options: string[];
  answer: string;
}

export interface QuestionnaireEntry {
  vacancyTitle: string;
  vacancyUrl: string;
  collectedAt: string;
  answers: CollectedQuestion[];
}

function typeOf(body: Element): QuestionType {
  if (qs('input[type="radio"]', body)) return "radio";
  if (qs('input[type="checkbox"]', body)) return "checkbox";
  if (qs("textarea", body)) return "textarea";
  if (qs('input[type="text"], input:not([type])', body)) return "input";
  return "unknown";
}

export function extractQuestions(root: ParentNode = document): CollectedQuestion[] {
  return qsa(SELECTORS.taskBody, root).map((body) => {
    const type = typeOf(body);
    const options =
      type === "radio" || type === "checkbox"
        ? qsa('[data-qa="cell-text-content"]', body).map((o) => normalizeText(o.textContent)).filter(Boolean)
        : [];
    return {
      question: normalizeText(qs(SELECTORS.taskQuestion, body)?.textContent),
      type,
      options,
      answer: "",
    };
  });
}
