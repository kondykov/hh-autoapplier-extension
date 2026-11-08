import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApplyPipeline, type ApplyResult, type PipelineDeps } from "../src/apply/pipeline";
import { attachLetterAndSubmit } from "../src/apply/submit";
import type { Vacancy } from "../src/core/state";
import { Logger } from "../src/infra/logger";
import { StaticTemplateProvider } from "../src/letters/letter-provider";
import { isDryRun, isQueueOn, type QueueItem } from "../src/queue/queue-mode";
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
    logger,
    letters: new StaticTemplateProvider(() => "Здравствуйте, {{company}}! Интересна вакансия {{title}}"),
    resumes: new ResumeSelector(logger),
    onApplied: vi.fn(async () => {}),
    onChange: vi.fn(async () => {}),
    dryRun: () => false,
    active: () => true,
    captchaHit: () => false,
    onSubmit: vi.fn(async () => {}),
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

  it("анкета без ответов: «outline», не переходит на поиск в режиме очереди", async () => {
    renderResponsePage({ questionnaire: true });
    const { deps, pipeline } = setup();

    expect(await run(pipeline.applyOnPage(vacancy()))).toBe("outline");
    expect(location.href).toBe(RESPONSE_URL);
    expect(submits).toBe(0);
  });

  // Живая страница 2026-10-25: подзаголовок про вопросы есть и у вакансий без анкеты
  it("подзаголовок «ответить на вопросы» без самих вопросов — не анкета, отклик уходит", async () => {
    renderResponsePage({ outline: true });
    const { deps, pipeline } = setup();

    expect(await run(pipeline.applyOnPage(vacancy()))).toBe("success");
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
    expect(deps.onApplied).not.toHaveBeenCalled();
  });

  it("match подходит к нескольким резюме — отклик не отправляется", async () => {
    renderResponsePage({ resumes: [PHP, DOTNET, "Senior C# .NET"] });
    const { pipeline } = setup();

    expect(await run(pipeline.applyOnPage(vacancy()))).toBe("failed");
    expect(submits).toBe(0);
    expect(resumeTitle()).toBe(PHP);
  });

  it("кнопка «Отправить» с aria-disabled — неактивна, клика нет", async () => {
    renderResponsePage();
    document.getElementById("submit")!.setAttribute("aria-disabled", "true");
    const { deps, pipeline } = setup();

    expect(await run(pipeline.applyOnPage(vacancy()))).toBe("disabled");
    expect(submits).toBe(0);
    expect(deps.onSubmit).not.toHaveBeenCalled();
  });

  it("aria-disabled=false — кнопка активна", async () => {
    renderResponsePage();
    document.getElementById("submit")!.setAttribute("aria-disabled", "false");
    const { pipeline } = setup();

    expect(await run(pipeline.applyOnPage(vacancy()))).toBe("success");
    expect(submits).toBe(1);
  });

  it("после нажатия ничего не изменилось — «Непонятное»", async () => {
    renderResponsePage({ afterSubmit: "nothing" });
    const { deps, pipeline } = setup();

    expect(await run(pipeline.applyOnPage(vacancy()))).toBe("unclear");
    expect(submits).toBe(1);
    expect(deps.onApplied).not.toHaveBeenCalled();
  });

  // #50: без поля письма «форма закрыта» было истинно с самого начала — success через 600 мс при любом исходе клика
  it("форма без поля письма: после клика ничего не изменилось — «Непонятное», а не отклик", async () => {
    renderResponsePage({ noLetter: true, afterSubmit: "nothing" });
    const { deps, pipeline } = setup();

    expect(await run(pipeline.applyOnPage(vacancy()))).toBe("unclear");
    expect(submits).toBe(1);
    expect(deps.onApplied).not.toHaveBeenCalled();
  });

  it("форма без поля письма: «Вы откликнулись» — отклик", async () => {
    renderResponsePage({ noLetter: true });
    const { deps, pipeline } = setup();

    expect(await run(pipeline.applyOnPage(vacancy()))).toBe("success");
    expect(deps.onApplied).toHaveBeenCalled();
  });

  // #50: смена страницы после клика запускала конвейер заново — до двух лишних нажатий
  it("после клика hh.ru сменил страницу, форма осталась — второго нажатия нет, «Непонятное»", async () => {
    renderResponsePage({ afterSubmit: "navigate" });
    const { deps, pipeline } = setup();

    expect(await run(pipeline.applyOnPage(vacancy()))).toBe("unclear");
    expect(submits).toBe(1);
    expect(deps.onApplied).not.toHaveBeenCalled();
  });

  it("после клика hh.ru сменил страницу на «Вы откликнулись» — отклик засчитан", async () => {
    renderResponsePage({ afterSubmit: "navigate-responded" });
    const { deps, pipeline } = setup();

    expect(await run(pipeline.applyOnPage(vacancy()))).toBe("success");
    expect(submits).toBe(1);
    expect(deps.onApplied).toHaveBeenCalled();
  });

  // #52: отметка «отправлено» и лимиты — до клика, в том числе для «Непонятного»
  it("onSubmit — один раз и до нажатия «Отправить»", async () => {
    renderResponsePage({ afterSubmit: "nothing" });
    let submitsAtMark = -1;
    const onSubmit = vi.fn(async () => {
      submitsAtMark = submits;
    });
    const { pipeline } = setup({ onSubmit });

    expect(await run(pipeline.applyOnPage(vacancy()))).toBe("unclear");
    expect(onSubmit).toHaveBeenCalledTimes(1);
    expect(submitsAtMark).toBe(0);
    expect(submits).toBe(1);
  });

  it("пробный режим — onSubmit не вызывается", async () => {
    renderResponsePage();
    const { deps, pipeline } = setup({ dryRun: () => true });

    expect(await run(pipeline.applyOnPage(vacancy()))).toBe("dry_run");
    expect(deps.onSubmit).not.toHaveBeenCalled();
  });

  // #52: капча проверялась только в начале resume — посреди вакансии конвейер кликал дальше
  describe("капча посреди вакансии", () => {
    it("во время выбора резюме — «Отправить» не нажата", async () => {
      renderResponsePage({ current: PHP });
      let captcha = false;
      const { deps, pipeline } = setup({ captchaHit: () => captcha });
      const choose = deps.resumes.choose.bind(deps.resumes);
      deps.resumes.choose = async (v) => {
        captcha = true;
        return choose(v);
      };

      expect(await run(pipeline.applyOnPage(vacancy()))).toBe("captcha");
      expect(submits).toBe(0);
      expect(deps.onSubmit).not.toHaveBeenCalled();
    });

    it("в паузе перед кликом «Отправить» — клика нет", async () => {
      renderResponsePage();
      let captcha = false;
      const { deps, pipeline } = setup({ captchaHit: () => captcha });
      vi.spyOn(Element.prototype, "scrollIntoView").mockImplementation(() => {
        captcha = true;
      });

      expect(await run(pipeline.applyOnPage(vacancy()))).toBe("captcha");
      expect(submits).toBe(0);
      expect(deps.onSubmit).not.toHaveBeenCalled();
    });
  });

  // #49: «Стоп очередь» посреди вакансии снимал пробный режим и «Отправить» нажималась по-настоящему
  describe("остановка очереди посреди вакансии", () => {
    // Те же формулы, что в Controller: состояние очереди перечитывается на каждом шаге
    function queueDeps(dryRunConfig: boolean) {
      const queue = { running: true, current: { id: "1", title: "", company: "", resume: null } as QueueItem | null };
      const deps: Partial<PipelineDeps> = {
        dryRun: () => isDryRun(queue.running, dryRunConfig),
        active: (v) => isQueueOn(queue.running, queue.current, v.link),
      };
      return { queue, deps };
    }

    for (const dryRunConfig of [true, false]) {
      it(`во время выбора резюме — «Отправить» не нажата (пробный режим: ${dryRunConfig})`, async () => {
        renderResponsePage({ current: PHP });
        const { queue, deps } = queueDeps(dryRunConfig);
        const { deps: all, pipeline } = setup(deps);
        const choose = all.resumes.choose.bind(all.resumes);
        all.resumes.choose = async (v) => {
          queue.running = false;
          return choose(v);
        };

        expect(await run(pipeline.applyOnPage(vacancy()))).toBe("stopped");
        expect(submits).toBe(0);
        expect(all.onApplied).not.toHaveBeenCalled();
      });
    }

    it("в паузе перед кликом «Отправить» — клика нет", async () => {
      renderResponsePage();
      const { queue, deps } = queueDeps(false);
      const { pipeline } = setup(deps);
      // scrollIntoView — сразу перед паузой humanClick
      vi.spyOn(Element.prototype, "scrollIntoView").mockImplementation(() => {
        queue.running = false;
      });

      expect(await run(pipeline.applyOnPage(vacancy()))).toBe("stopped");
      expect(submits).toBe(0);
    });

    it("очередь перезапущена с другой вакансией — эту не отправляет", async () => {
      renderResponsePage();
      const { queue, deps } = queueDeps(false);
      const { pipeline } = setup(deps);
      queue.current = { id: "2", title: "", company: "", resume: null };

      expect(await run(pipeline.applyOnPage(vacancy()))).toBe("stopped");
      expect(submits).toBe(0);
    });
  });

  it("страница отклика без формы (вакансия в архиве) — failed без кликов", async () => {
    document.body.innerHTML = "<main>Вакансия в архиве</main>";
    const { deps, pipeline } = setup();

    expect(await run(pipeline.applyOnPage(vacancy()))).toBe("failed");
    expect(deps.onApplied).not.toHaveBeenCalled();
  });
});

describe("attachLetterAndSubmit", () => {
  beforeEach(() => setUrl(RESPONSE_URL));

  it("нет кнопки «Отправить» — failed, письмо всё равно вставлено", async () => {
    document.body.innerHTML = `<textarea data-qa="vacancy-response-popup-form-letter-input"></textarea>`;
    const result = attachLetterAndSubmit("Письмо", new Logger());
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
    const result = attachLetterAndSubmit("Письмо", new Logger());
    await vi.runAllTimersAsync();
    expect(await result).toBe("success");
    expect(clicked).toBe(true);
  });
});
