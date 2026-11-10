import { SELECTORS } from "../core/selectors";

// Признак архива со страницы вакансии (#70). Форма отклика архивной вакансии открывается как обычно — с резюме,
// письмом и кнопкой отправки, признака там нет (проверено 2026-11-10). Страница /vacancy/{id} отдаётся с кодом 200
// и со входом, и без; признак — status.archived в JSON HH-Lux-InitialState, closedForApplicants у архивных false
export type ArchiveCheck = { state: "archived" } | { state: "open" } | { state: "unknown"; error: string };

export interface ArchiveChecker {
  check(id: string): Promise<ArchiveCheck>;
}

// Архив — только явное status.archived === true у той же вакансии; всё остальное — не архив или «не знаю»
export function parseArchiveState(html: string, id: string): ArchiveCheck {
  const doc = new DOMParser().parseFromString(html, "text/html");
  const el = doc.querySelector<HTMLTemplateElement>(SELECTORS.pageInitialState);
  // В браузере текст шаблона лежит в content, а не в самом элементе
  const json = el?.content?.textContent || el?.textContent;
  if (!json) return { state: "unknown", error: "на странице нет данных" };

  let vacancy: { vacancyId?: unknown; status?: { archived?: unknown } } | undefined;
  try {
    vacancy = JSON.parse(json)?.vacancyView?.vacancyFull?.vacancy;
  } catch {
    return { state: "unknown", error: "данные страницы не разобраны" };
  }
  if (!vacancy) return { state: "unknown", error: "на странице нет вакансии" };
  if (String(vacancy.vacancyId) !== id) return { state: "unknown", error: `на странице другая вакансия ${String(vacancy.vacancyId)}` };

  const archived = vacancy.status?.archived;
  if (archived === true) return { state: "archived" };
  if (archived === false) return { state: "open" };
  return { state: "unknown", error: "нет признака архива" };
}

// Один GET без cookie: страница публичная, и запрос не попадает в историю просмотров владельца
export function pageArchiveChecker(origin: () => string, fetchFn: typeof fetch = (input, init) => fetch(input, init), timeoutMs = 10000): ArchiveChecker {
  return {
    async check(id) {
      const abort = new AbortController();
      const timer = setTimeout(() => abort.abort(), timeoutMs);
      try {
        const res = await fetchFn(new URL(`/vacancy/${encodeURIComponent(id)}`, origin()).toString(), { credentials: "omit", signal: abort.signal });
        if (!res.ok) return { state: "unknown", error: `ответ ${res.status}` };
        return parseArchiveState(await res.text(), id);
      } catch (e) {
        return { state: "unknown", error: abort.signal.aborted ? "нет ответа" : (e as Error).message };
      } finally {
        clearTimeout(timer);
      }
    },
  };
}
