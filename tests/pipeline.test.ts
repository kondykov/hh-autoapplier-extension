import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApplyPipeline, type ApplyResult, type PipelineDeps } from "../src/apply/pipeline";
import { attachLetterAndSubmit } from "../src/apply/submit";
import { AppState, type Vacancy } from "../src/core/state";
import { Logger } from "../src/infra/logger";
import { StaticTemplateProvider } from "../src/letters/letter-provider";
import { ResumeSelector } from "../src/resume/selector";
import {
  DOTNET,
  letterValue,
  PHP,
  renderResponsePage,
  resetSubmits,
  resumeTitle,
  submits,
} from "./fixtures/response-form";

const setUrl = (url: string) => (window as unknown as { happyDOM: { setURL(url: string): void } }).happyDOM.setURL(url);

const RESPONSE_URL = "https://hh.ru/applicant/vacancy_response?vacancyId=1";

function setup(overrides: Partial<PipelineDeps> = {}) {
  const logger = new Logger();
  const deps: PipelineDeps = {
    state: new AppState(),
    logger,
    letters: new StaticTemplateProvider(() => "Здравствуйте, {{company}}! Интересна вакансия {{title}}"),
    resumes: new ResumeSelector(logger),
    onApplied: vi.fn(async () => {}),
    onChange: vi.fn(async () => {}),
    dryRun: () => false,
    ...overrides,
  };
  return { deps, pipeline: new ApplyPipeline(deps) };
}

const vacancy = (patch: Partial<Vacancy> = {}): Vacancy => ({
  title: "Старший разработчик C# .NET",
  company: "Acme",
  link: RESPONSE_URL,
  // Резюме выбирает backend: имя профиля и его match на hh.ru
  resume: ".NET",
  resumeMatch: "C# .NET",
  ...patch,
});

// Конвейер ждёт таймерами (анимации, подтверждение исхода) — прокручиваем их
async function run(promise: Promise<ApplyResult>): Promise<ApplyResult> {
  await vi.runAllTimersAsync();
  return promise;
}

beforeEach(() => {
  vi.useFakeTimers();
  resetSubmits();
  document.body.innerHTML = "";
  Element.prototype.scrollIntoView ??= () => {};
});

afterEach(() => vi.useRealTimers());

describe("ApplyPipeline.applyOnPage — страница отклика", () => {
  beforeEach(() => setUrl(RESPONSE_URL));

  it("выбирает резюме сервера, вставляет письмо и отправляет", async () => {
    renderResponsePage();
    const { deps, pipeline } = setup();
    const v = vacancy();

    expect(await run(pipeline.applyOnPage(v))).toBe("success");
    expect(submits).toBe(1);
    expect(v.resume).toBe(".NET");
    expect(deps.onApplied).toHaveBeenCalledWith(v);
  });

  it("резюме сервера — по его match, а не по стеку вакансии; письмо — с подстановками", async () => {
    renderResponsePage({ current: DOTNET, afterSubmit: "nothing" });
    const { pipeline } = setup({ dryRun: () => true });
    const v = vacancy({ resume: "PHP", resumeMatch: "PHP" });

    expect(await run(pipeline.applyOnPage(v))).toBe("dry_run");
    expect(resumeTitle()).toBe(PHP);
    expect(letterValue()).toBe("Здравствуйте, Acme! Интересна вакансия Старший разработчик C# .NET");
  });

  it("сервер не выбрал резюме — остаётся выбранное hh.ru", async () => {
    renderResponsePage({ current: PHP, afterSubmit: "nothing" });
    const { pipeline } = setup({ dryRun: () => true });

    expect(await run(pipeline.applyOnPage(vacancy({ resume: undefined, resumeMatch: undefined })))).toBe("dry_run");
    expect(resumeTitle()).toBe(PHP);
  });

  it("раскрывает поле письма, если оно спрятано за кнопкой", async () => {
    renderResponsePage({ letterVisible: false, afterSubmit: "nothing" });
    const { pipeline } = setup({ dryRun: () => true });

    expect(await run(pipeline.applyOnPage(vacancy()))).toBe("dry_run");
    expect(letterValue()).toContain("Здравствуйте, Acme!");
  });

  it("пробный режим не нажимает «Отправить» и не регистрирует отклик", async () => {
    renderResponsePage();
    const { deps, pipeline } = setup({ dryRun: () => true });

    expect(await run(pipeline.applyOnPage(vacancy()))).toBe("dry_run");
    expect(submits).toBe(0);
    expect(deps.onApplied).not.toHaveBeenCalled();
  });

  it("анкета: кладёт в аутлайн и не переходит на поиск в режиме очереди", async () => {
    renderResponsePage({ questionnaire: true });
    const { deps, pipeline } = setup();

    expect(await run(pipeline.applyOnPage(vacancy()))).toBe("outline");
    expect(deps.state.outline).toHaveLength(1);
    expect(location.href).toBe(RESPONSE_URL);
    expect(submits).toBe(0);
  });

  // Живая страница 2026-10-25: подзаголовок про вопросы есть и у вакансий без анкеты
  it("подзаголовок «ответить на вопросы» без самих вопросов — не анкета, отклик уходит", async () => {
    renderResponsePage({ outline: true });
    const { deps, pipeline } = setup();

    expect(await run(pipeline.applyOnPage(vacancy()))).toBe("success");
    expect(deps.state.outline).toHaveLength(0);
    expect(submits).toBe(1);
  });

  it("уже откликались: засчитывает отклик без повторной отправки", async () => {
    renderResponsePage({ responded: true });
    const { deps, pipeline } = setup();

    expect(await run(pipeline.applyOnPage(vacancy()))).toBe("success");
    expect(deps.onApplied).toHaveBeenCalled();
    expect(submits).toBe(0);
  });

  it("неактивная кнопка «Отправить» — остановка для человека", async () => {
    renderResponsePage({ submitDisabled: true });
    const { deps, pipeline } = setup();

    expect(await run(pipeline.applyOnPage(vacancy()))).toBe("disabled");
    expect(deps.onApplied).not.toHaveBeenCalled();
  });

  it("нужного резюме нет в списке — отклик не отправляется", async () => {
    renderResponsePage({ resumes: [PHP] });
    const { deps, pipeline } = setup();

    expect(await run(pipeline.applyOnPage(vacancy()))).toBe("failed");
    expect(submits).toBe(0);
    expect(deps.state.skipped).toBe(1);
  });

  it("после нажатия ничего не изменилось — «Непонятное»", async () => {
    renderResponsePage({ afterSubmit: "nothing" });
    const { deps, pipeline } = setup();

    expect(await run(pipeline.applyOnPage(vacancy()))).toBe("unclear");
    expect(submits).toBe(1);
    expect(deps.state.unclear).toHaveLength(1);
    expect(deps.onApplied).not.toHaveBeenCalled();
  });

  it("страница отклика без формы (вакансия в архиве) — failed без кликов", async () => {
    document.body.innerHTML = "<main>Вакансия в архиве</main>";
    const { deps, pipeline } = setup();

    expect(await run(pipeline.applyOnPage(vacancy()))).toBe("failed");
    expect(deps.onApplied).not.toHaveBeenCalled();
    expect(deps.state.skipped).toBe(1);
  });
});

describe("attachLetterAndSubmit", () => {
  beforeEach(() => setUrl(RESPONSE_URL));

  it("нет кнопки «Отправить» — failed, письмо всё равно вставлено", async () => {
    document.body.innerHTML = `<textarea data-qa="vacancy-response-popup-form-letter-input"></textarea>`;
    const result = attachLetterAndSubmit(null, "Письмо", new Logger());
    await vi.runAllTimersAsync();
    expect(await result).toBe("failed");
    expect(letterValue()).toBe("Письмо");
  });

  it("находит кнопку по data-qa окна отклика", async () => {
    document.body.innerHTML = `<div data-qa="vacancy-response-popup"><textarea data-qa="vacancy-response-popup-form-letter-input"></textarea>
      <button data-qa="vacancy-response-submit-popup">Откликнуться</button></div>`;
    let clicked = false;
    document.querySelector("button")!.onclick = () => {
      clicked = true;
      document.querySelector('[data-qa="vacancy-response-popup"]')!.remove();
    };
    const result = attachLetterAndSubmit(null, "Письмо", new Logger());
    await vi.runAllTimersAsync();
    expect(await result).toBe("success");
    expect(clicked).toBe(true);
  });
});
