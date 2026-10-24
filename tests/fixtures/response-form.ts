// Разметка формы отклика hh.ru по живой странице (2026-10-24): резюме — [role=button] с [data-qa=resume-title],
// варианты — [role=option], на странице отклика кнопка «Откликнуться» без data-qa, анкета — [data-qa=title-description]

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
  outline?: boolean;
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
  const outline = o.outline ? `<div data-qa="title-description">Для отклика необходимо ответить на несколько вопросов работодателя</div>` : "";
  return `
    <h1 data-qa="title">Отклик на вакансию</h1>${outline}
    ${resumeBlock(o.current ?? PHP, o.resumes ?? [PHP, DOTNET])}
    ${letterBlock(o.letterVisible ?? true)}
    <button id="submit"${o.submitDisabled ? " disabled" : ""}>Откликнуться</button>`;
}

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
