import type { ApplyResult } from "../apply/pipeline";
import type { QuestionAnswer } from "../questions/fill";

// Вакансия из очереди backend (GET /api/queue): только то, что нужно для отклика
export interface QueueItem {
  id: string;
  title: string;
  company: string;
  resume: string | null;
  // Часть названия резюме на hh.ru (match профиля с сервера) — заполняет очередь перед открытием вакансии
  resumeMatch?: string;
  // Письмо под вакансию от LLM backend; нет — расширение берёт свой шаблон
  letter?: string;
  // Ответы LLM на анкету работодателя; есть — конвейер заполнит анкету вместо пропуска
  answers?: QuestionAnswer[];
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

// Пробный режим очереди — fail-closed: остановленная очередь не отправляет никогда (#49)
export const isDryRun = (queueRunning: boolean, queueDryRun: boolean): boolean => !queueRunning || queueDryRun;

// Очередь всё ещё ведёт вакансию этой страницы: её не остановили и не перезапустили с другой вакансией
export function isQueueOn(queueRunning: boolean, current: QueueItem | null, link: string): boolean {
  if (!queueRunning || !current) return false;
  try {
    return isPageOf(link, current.id);
  } catch {
    return false;
  }
}

// Первая вакансия очереди, которую ещё не пробовали в этом запуске
export const pickNext = (queue: QueueItem[], tried: string[]): QueueItem | null =>
  queue.find((v) => !tried.includes(v.id)) ?? null;

// Сколько неудач подряд очередь терпит, прежде чем остановиться (#75)
export const QUEUE_FAIL_LIMIT = 3;

export const isQueueFailure = (result: ApplyResult): boolean => result === "skipped" || result === "failed" || result === "unclear";

// Исход конвейера → статус на сервере. null — серверу ничего не сообщаем (пробный прогон, ручное вмешательство)
export function toStatusReport(result: ApplyResult): StatusReport | null {
  switch (result) {
    case "success":
      return { status: "Applied", reason: null };
    case "outline":
      // Сюда попадает только анкета, вопросы которой не удалось прочитать: остальные уходят на сервер и возвращаются с ответами
      return { status: "Skipped", reason: "анкета работодателя — вопросы не найдены на странице" };
    case "skipped":
      return { status: "Failed", reason: "форма отклика не открылась" };
    case "failed":
      return { status: "Failed", reason: "не удалось выбрать резюме или отправить отклик" };
    case "unclear":
      return { status: "Failed", reason: "отклик не подтверждён — проверить вручную" };
    case "disabled":
    case "dry_run":
    case "stopped":
    case "captcha":
      return null;
  }
}
