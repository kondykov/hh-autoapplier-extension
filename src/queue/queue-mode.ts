import type { ApplyResult } from "../apply/pipeline";

// Вакансия из очереди backend (GET /api/queue): только то, что нужно для отклика
export interface QueueItem {
  id: string;
  title: string;
  company: string;
  resume: string | null;
}

export type VacancyStatus = "Applied" | "Skipped" | "Failed";

export interface StatusReport {
  status: VacancyStatus;
  reason: string | null;
}

// Страница отклика сразу с формой: резюме, письмо, кнопка отправки (или анкета работодателя)
export const responseUrl = (id: string) => `https://hh.ru/applicant/vacancy_response?vacancyId=${encodeURIComponent(id)}`;

// Вакансия с этой страницы — та, что сейчас в работе (hh.ru может перенаправить на региональный поддомен)
export function isPageOf(url: string, id: string): boolean {
  const vacancyId = new URL(url).searchParams.get("vacancyId") ?? url.match(/\/vacancy\/(\d+)/)?.[1];
  return vacancyId === id;
}

// Первая вакансия очереди, которую ещё не пробовали в этом запуске
export const pickNext = (queue: QueueItem[], tried: string[]): QueueItem | null =>
  queue.find((v) => !tried.includes(v.id)) ?? null;

// Исход конвейера → статус на сервере. null — серверу ничего не сообщаем (пробный прогон, ручное вмешательство)
export function toStatusReport(result: ApplyResult): StatusReport | null {
  switch (result) {
    case "success":
      return { status: "Applied", reason: null };
    case "outline":
      return { status: "Skipped", reason: "анкета работодателя — вопросы сохранены в расширении" };
    case "skipped":
      return { status: "Failed", reason: "форма отклика не открылась" };
    case "failed":
      return { status: "Failed", reason: "не удалось выбрать резюме или отправить отклик" };
    case "unclear":
      return { status: "Failed", reason: "отклик не подтверждён — проверить вручную" };
    case "disabled":
    case "dry_run":
      return null;
  }
}
