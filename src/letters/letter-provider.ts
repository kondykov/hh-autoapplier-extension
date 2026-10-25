import type { Vacancy } from "../core/state";

// Точка расширения: статический шаблон или готовое письмо под вакансию от backend (DIP)
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

// Письмо, которое backend написал под вакансию (LLM), приоритетнее шаблона
export class PreparedLetterProvider implements LetterProvider {
  constructor(private fallback: LetterProvider) {}

  async getLetter(vacancy: Vacancy): Promise<string> {
    return vacancy.letter?.trim() || this.fallback.getLetter(vacancy);
  }
}
