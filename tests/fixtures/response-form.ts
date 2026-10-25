// Разметка формы отклика hh.ru по живой странице (2026-10-24): резюме — [role=button] с [data-qa=resume-title],
// варианты — [role=option], на странице отклика кнопка «Откликнуться» без data-qa; анкета — блоки [data-qa=task-body] (2026-10-25)

export const PHP = "Fullstack Software Engineer | PHP / Nuxt";
export const DOTNET = "Backend Software Engineer | C# .NET developer130 000 ₽";

export interface FormOptions {
  resumes?: string[];
  current?: string;
  // Поле письма сразу видно; иначе — за кнопкой «Приложить письмо»
  letterVisible?: boolean;
  submitDisabled?: boolean;
  // Что делает hh.ru после нажатия «Отправить»
  afterSubmit?: "responded" | "close" | "nothing";
  // Подзаголовок «Для отклика необходимо ответить на несколько вопросов»: на странице отклика он есть и без анкеты,
  // во всплывающем окне — признак анкеты
  outline?: boolean;
  // Анкета работодателя прямо в форме (страница отклика): тест с одним вариантом, согласие, свободный текст
  questionnaire?: boolean;
  responded?: boolean;
}

const resumeBlock = (current: string, resumes: string[]) => `
  <div role="button" id="resume-trigger"><span data-qa="resume-title">${current}</span></div>
  <div role="listbox">${resumes.map((r) => `<div role="option">${r}</div>`).join("")}</div>`;

const letterBlock = (visible: boolean) =>
  visible
    ? `<textarea data-qa="vacancy-response-popup-form-letter-input"></textarea>`
    : `<button data-qa="vacancy-response-letter-toggle" id="letter-toggle">Приложить письмо</button>`;

function body(o: FormOptions): string {
  if (o.responded) return `<h1>Отклик на вакансию</h1><div>Вы откликнулись</div>`;
  if (o.questionnaire) o = { letterVisible: false, ...o };
  const outline = o.outline ? `<div data-qa="title-description">Для отклика необходимо ответить на несколько вопросов работодателя</div>` : "";
  return `
    <h1 data-qa="title">Отклик на вакансию</h1>${outline}
    ${o.questionnaire ? questionnaireBlock : ""}
    ${resumeBlock(o.current ?? PHP, o.resumes ?? [PHP, DOTNET])}
    ${letterBlock(o.letterVisible ?? true)}
    <button id="submit"${o.submitDisabled ? " disabled" : ""}>Откликнуться</button>`;
}

// Разметка анкеты — по живой странице отклика (2026-10-25), без классов и лишних обёрток
const option = (name: string, type: "radio" | "checkbox", value: string, text: string) => `
  <label data-qa="cell"><div data-qa="cell-left-side"><span data-qa="${type}-container"><input name="${name}" type="${type}" value="${value}"><span data-qa="${type}"></span></span></div>
  <div data-qa="cell-text"><span data-qa="cell-text-content">${text}</span></div></label>`;

export const QUESTIONS = {
  solid: "Какой принцип SOLID описывает зависимость от абстракций, а не от реализаций?",
  agree: "Вы принимаете это условие?",
  salary: "Укажите, пожалуйста, Ваши зарплатные ожидания?",
};

const questionnaireBlock = `
  <div data-qa="test-description">Пожалуйста, пройдите антиспам тест</div>
  <div data-qa="employer-asking-for-test"><h2 data-qa="title">Ответьте на&nbsp;вопросы</h2></div>
  <div data-qa="task-body"><div data-qa="task-question">${QUESTIONS.solid}</div><div>
    ${option("task_1", "radio", "11", "Single Responsibility Principle")}${option("task_1", "radio", "12", "Dependency Inversion Principle")}</div></div>
  <div data-qa="task-body"><div data-qa="task-question">&nbsp;${QUESTIONS.agree}</div><div>
    ${option("task_2", "checkbox", "21", "Да")}${option("task_2", "checkbox", "22", "Нет")}</div></div>
  <div data-qa="task-body"><div data-qa="task-question"><p>${QUESTIONS.salary}</p></div>
    <div data-qa="textarea-wrapper"><div data-qa="textarea-native-wrapper"><textarea name="task_3_text" type="textarea" rows="4"></textarea><pre></pre></div></div></div>`;

// Страница /applicant/vacancy_response
export function renderResponsePage(o: FormOptions = {}): void {
  document.body.innerHTML = `<main id="form">${body(o)}</main>`;
  wire(o, () => {
    document.getElementById("form")!.innerHTML = "<div>Вы откликнулись</div>";
  });
}

// Всплывающее окно отклика поверх выдачи
export function renderResponseModal(o: FormOptions = {}): void {
  const modal = document.createElement("div");
  modal.setAttribute("data-qa", "vacancy-response-popup");
  modal.innerHTML = `${body(o)}<button data-qa="response-popup-close">×</button>`;
  document.body.append(modal);
  wire(o, () => modal.remove());
}

// Поведение элементов: выбор резюме, раскрытие письма, отправка
function wire(o: FormOptions, close: () => void): void {
  for (const option of Array.from(document.querySelectorAll<HTMLElement>('[role="option"]')))
    option.onclick = () => {
      document.querySelector('[data-qa="resume-title"]')!.textContent = option.textContent;
    };

  const toggle = document.getElementById("letter-toggle");
  if (toggle)
    toggle.onclick = () => {
      toggle.insertAdjacentHTML("afterend", `<textarea data-qa="vacancy-response-popup-form-letter-input"></textarea>`);
      toggle.remove();
    };

  const submit = document.getElementById("submit");
  if (!submit) return;
  submit.onclick = () => {
    submits++;
    if (o.afterSubmit === "nothing") return;
    if (o.afterSubmit === "close") close();
    else document.body.innerHTML = "<main><div>Вы откликнулись</div></main>";
  };
}

export let submits = 0;

export function resetSubmits(): void {
  submits = 0;
}

export const letterValue = () =>
  document.querySelector<HTMLTextAreaElement>('[data-qa="vacancy-response-popup-form-letter-input"]')?.value ?? null;

export const resumeTitle = () => document.querySelector('[data-qa="resume-title"]')?.textContent ?? null;
