import { DELAYS, wait } from "../core/delays";
import { SELECTORS } from "../core/selectors";
import type { Vacancy } from "../core/state";
import { normalizeText, qsa } from "../dom/query";
import type { Logger } from "../infra/logger";
import { pickProfile, type ResumeProfile } from "./profiles";

const currentTitle = () => normalizeText(document.querySelector(SELECTORS.resumeTitle)?.textContent);

const trigger = () =>
  qsa<HTMLElement>(SELECTORS.resumeTitle)
    .map((t) => t.closest<HTMLElement>(SELECTORS.resumeTrigger))
    .find(Boolean) ?? null;

// failed: нужное резюме определено, но выбрать его не удалось — отправлять отклик нельзя
export interface ChooseResult {
  profile: ResumeProfile | null;
  failed: boolean;
}

const ok = (profile: ResumeProfile | null): ChooseResult => ({ profile, failed: false });
const fail = (): ChooseResult => ({ profile: null, failed: true });

export class ResumeSelector {
  constructor(
    private profiles: () => ResumeProfile[],
    private logger: Logger,
  ) {}

  // Выбирает в форме отклика резюме: заданное заранее (vacancy.resume — например, выбор backend) или по стеку вакансии
  async choose(vacancy: Vacancy): Promise<ChooseResult> {
    const haystack = `${vacancy.title} ${vacancy.text ?? ""} ${(vacancy.stack ?? []).join(" ")}`;
    const preset = vacancy.resume ? this.profiles().find((p) => p.name === vacancy.resume) : undefined;
    const profile = preset ?? pickProfile(this.profiles(), haystack);
    if (!profile) {
      this.logger.log("Стек не указывает на конкретное резюме, оставляю выбранное hh.ru", "warn");
      return ok(null);
    }
    if (currentTitle().includes(profile.match)) return ok(profile);

    const opener = trigger();
    if (!opener) {
      this.logger.log("Выбор резюме недоступен (резюме одно)", "warn");
      return ok(null);
    }
    opener.click();
    await wait(DELAYS.animation);
    const option = qsa<HTMLElement>(SELECTORS.resumeOption).find((o) => normalizeText(o.textContent).includes(profile.match));
    if (!option) {
      this.logger.log(`Резюме «${profile.match}» не найдено в списке`, "error");
      document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
      return fail();
    }
    option.click();
    await wait(DELAYS.animation);

    if (!currentTitle().includes(profile.match)) {
      this.logger.log(`Не удалось выбрать резюме «${profile.match}»`, "error");
      return fail();
    }
    this.logger.log(`Резюме: ${profile.name}`, "success");
    return ok(profile);
  }
}
