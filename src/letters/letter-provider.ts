import type { Vacancy } from "../core/state";

// Точка расширения: статический шаблон сейчас, LLM-доработка по тексту вакансии позже (DIP)
export interface LetterProvider {
  getLetter(vacancy: Vacancy): Promise<string>;
}

export class StaticTemplateProvider implements LetterProvider {
  constructor(private template: () => string) {}

  async getLetter(vacancy?: Vacancy): Promise<string> {
    return this.template()
      .replaceAll("{{title}}", vacancy?.title ?? "")
      .replaceAll("{{company}}", vacancy?.company ?? "");
  }
}
