import { SELECTORS } from "../core/selectors";
import { fillTextarea } from "../dom/cover-letter";
import { setNativeValue } from "../dom/input";
import { normalizeText, qs, qsa } from "../dom/query";
import { extractQuestions, type CollectedQuestion, type QuestionType } from "./extract";
import { humanClick } from "../dom/input";

// Ответ backend на вопрос анкеты: варианты дословно (radio — один, checkbox — несколько) или текст одной строкой
export interface QuestionAnswer {
  question: string;
  answer: string[];
}

export interface FillResult {
  ok: boolean;
  // Вопросы на странице, на которые нет ответа или вариант не найден
  missing: string[];
}

const same = (a: string, b: string) => normalizeText(a).toLowerCase() === normalizeText(b).toLowerCase();

// Заполнить анкету на странице ответами backend. Вопрос ищется по тексту, вариант — по тексту в label.
// Если хоть на один вопрос нет ответа — ok: false, отправлять такую форму нельзя
export async function fillQuestionnaire(answers: QuestionAnswer[], root: ParentNode = document): Promise<FillResult> {
  const bodies = qsa(SELECTORS.taskBody, root);
  // Анкета есть (employer-asking-for-test), а блоков вопросов нет: заполнять нечего, отправлять нельзя
  if (bodies.length === 0) return { ok: false, missing: [] };

  const missing: string[] = [];
  for (const body of bodies) {
    const question = normalizeText(qs(SELECTORS.taskQuestion, body)?.textContent);
    const answer = answers.find((a) => same(a.question, question))?.answer ?? [];
    if (answer.length === 0 || !(await fillOne(body, answer))) missing.push(question);
  }
  return { ok: missing.length === 0, missing };
}

async function fillOne(body: Element, answer: string[]): Promise<boolean> {
  const options = qsa<HTMLLabelElement>(SELECTORS.taskOption, body);
  if (options.length > 0) {
    const inputs = answer.map((value) => {
      const label = options.find((o) => same(qs(SELECTORS.taskOptionText, o)?.textContent ?? "", value));
      return label ? qs<HTMLInputElement>("input", label) : null;
    });
    if (inputs.some((i) => !i)) return false;
    // Заранее отмеченные чекбоксы вне ответа снимаются, иначе уйдут вместе с ним
    for (const option of options) {
      const checkbox = qs<HTMLInputElement>(SELECTORS.checkboxInput, option);
      if (checkbox?.checked && !inputs.includes(checkbox)) await humanClick(checkbox);
    }
    for (const input of inputs) if (!input!.checked) await humanClick(input!);
    return true;
  }

  const textarea = qs<HTMLTextAreaElement>("textarea", body);
  if (textarea) {
    fillTextarea(textarea, answer.join("\n"));
    return true;
  }
  const input = qs<HTMLInputElement>('input[type="text"], input:not([type])', body);
  if (input) {
    setNativeValue(input, answer.join(" "));
    return true;
  }
  return false;
}

// Тип вопроса в терминах backend
const SERVER_TYPES: Record<QuestionType, string> = {
  radio: "Radio",
  checkbox: "Checkbox",
  textarea: "Text",
  input: "Text",
  unknown: "Unknown",
};

export interface ServerQuestion {
  question: string;
  type: string;
  options: string[];
}

export const toServerQuestions = (questions: CollectedQuestion[] = extractQuestions()): ServerQuestion[] =>
  questions.filter((q) => q.question).map((q) => ({ question: q.question, type: SERVER_TYPES[q.type], options: q.options }));
