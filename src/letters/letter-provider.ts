import type { Vacancy } from "../core/state";

// Точка расширения: статический шаблон сейчас, LLM-доработка по тексту вакансии позже (DIP)
export interface LetterProvider {
  getLetter(vacancy: Vacancy): Promise<string>;
}

export class StaticTemplateProvider implements LetterProvider {
  constructor(private template: (vacancy?: Vacancy) => string) {}

  async getLetter(vacancy?: Vacancy): Promise<string> {
    return this.template(vacancy)
      .replaceAll("{{title}}", vacancy?.title ?? "")
      .replaceAll("{{company}}", vacancy?.company ?? "");
  }
}
