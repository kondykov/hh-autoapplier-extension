import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApplyPipeline, type ApplyResult, type PipelineDeps } from "../src/apply/pipeline";
import { attachLetterAndSubmit } from "../src/apply/submit";
import { AppState, type Vacancy } from "../src/core/state";
import { Logger } from "../src/infra/logger";
import { StaticTemplateProvider } from "../src/letters/letter-provider";
import { DEFAULT_RESUMES } from "../src/resume/profiles";
import { ResumeSelector } from "../src/resume/selector";
import {
  DOTNET,
  letterValue,
  PHP,
  renderResponseModal,
  renderResponsePage,
  resetSubmits,
  resumeTitle,
  submits,
} from "./fixtures/response-form";

const setUrl = (url: string) => (window as unknown as { happyDOM: { setURL(url: string): void } }).happyDOM.setURL(url);

const RESPONSE_URL = "https://hh.ru/applicant/vacancy_response?vacancyId=1";
const SEARCH_URL = "https://hh.ru/search/vacancy?text=C%23";

function setup(overrides: Partial<PipelineDeps> = {}) {
  const logger = new Logger();
  const deps: PipelineDeps = {
    state: new AppState(),
    logger,
    letters: new StaticTemplateProvider(() => "Здравствуйте, {{company}}! Интересна вакансия {{title}}"),
    resumes: new ResumeSelector(() => DEFAULT_RESUMES, logger),
    searchUrl: () => SEARCH_URL,
    onApplied: vi.fn(async () => {}),
    onOutline: vi.fn(async () => {}),
    onChange: vi.fn(async () => {}),
    dryRun: () => false,
    stayOnOutline: () => true,
    ...overrides,
  };
  return { deps, pipeline: new ApplyPipeline(deps) };
}

const vacancy = (patch: Partial<Vacancy> = {}): Vacancy => ({
  title: "Старший разработчик C# .NET",
  company: "Acme",
  link: RESPONSE_URL,
  card: null,
  applyBtn: null,
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

  it("выбирает резюме по стеку, вставляет письмо и отправляет", async () => {
    renderResponsePage();
    const { deps, pipeline } = setup();
    const v = vacancy();

    expect(await run(pipeline.applyOnPage(v))).toBe("success");
    expect(submits).toBe(1);
    expect(v.resume).toBe(".NET");
    expect(deps.onApplied).toHaveBeenCalledWith(v);
  });

  it("резюме, выбранное заранее (backend), важнее стека; письмо — с подстановками", async () => {
    renderResponsePage({ current: DOTNET, afterSubmit: "nothing" });
    const { pipeline } = setup({ dryRun: () => true });
    const v = vacancy({ resume: "PHP" });

    expect(await run(pipeline.applyOnPage(v))).toBe("dry_run");
    expect(resumeTitle()).toBe(PHP);
    expect(letterValue()).toBe("Здравствуйте, Acme! Интересна вакансия Старший разработчик C# .NET");
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

  it("анкета: сохраняет вопросы, кладёт в аутлайн и не переходит на поиск в режиме очереди", async () => {
    renderResponsePage({ outline: true });
    const { deps, pipeline } = setup();

    expect(await run(pipeline.applyOnPage(vacancy()))).toBe("outline");
    expect(deps.onOutline).toHaveBeenCalled();
    expect(deps.state.outline).toHaveLength(1);
    expect(location.href).toBe(RESPONSE_URL);
    expect(submits).toBe(0);
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

describe("ApplyPipeline.applyFromSearch — окно поверх выдачи", () => {
  beforeEach(() => setUrl(SEARCH_URL));

  const card = (onApply: () => void) => {
    document.body.innerHTML = `<div data-qa="vacancy-serp__vacancy"><a data-qa="vacancy-serp__vacancy_response">Откликнуться</a></div>`;
    const btn = document.querySelector<HTMLElement>('[data-qa="vacancy-serp__vacancy_response"]')!;
    btn.onclick = onApply;
    return { card: btn.parentElement!, applyBtn: btn };
  };

  it("открывает окно, выбирает резюме, отправляет; окно закрылось — отклик принят", async () => {
    const { deps, pipeline } = setup();
    const v = vacancy({ link: "https://hh.ru/vacancy/1", ...card(() => renderResponseModal({ afterSubmit: "close" })) });

    expect(await run(pipeline.applyFromSearch(v))).toBe("success");
    expect(submits).toBe(1);
    expect(v.resume).toBe(".NET");
    expect(deps.onApplied).toHaveBeenCalledWith(v);
  });

  it("анкета в окне: закрывает окно и остаётся на выдаче", async () => {
    const { deps, pipeline } = setup({ stayOnOutline: () => false });
    const v = vacancy({ link: "https://hh.ru/vacancy/1", ...card(() => renderResponseModal({ outline: true })) });

    expect(await run(pipeline.applyFromSearch(v))).toBe("outline");
    expect(deps.state.outline).toHaveLength(1);
    expect(location.href).toBe(SEARCH_URL);
    expect(submits).toBe(0);
  });

  it("после клика по «Откликнуться» ничего не открылось — пропуск", async () => {
    const { deps, pipeline } = setup();
    const v = vacancy({ link: "https://hh.ru/vacancy/1", ...card(() => {}) });

    expect(await run(pipeline.applyFromSearch(v))).toBe("skipped");
    expect(deps.state.skipped).toBe(1);
    expect(submits).toBe(0);
  });

  it("без кнопки отклика — пропуск без кликов", async () => {
    const { deps, pipeline } = setup();
    expect(await run(pipeline.applyFromSearch(vacancy()))).toBe("skipped");
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
