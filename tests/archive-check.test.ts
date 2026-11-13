import { describe, expect, it, vi } from "vitest";
import { pageArchiveChecker, parseArchiveState } from "../src/apply/archive-check";
import { vacancyPageHtml } from "./fixtures/vacancy-page";

describe("parseArchiveState (#70)", () => {
  it("архивная страница — archived, открытая — open", () => {
    expect(parseArchiveState(vacancyPageHtml("138137097", true), "138137097")).toEqual({ state: "archived" });
    expect(parseArchiveState(vacancyPageHtml("138922163", false), "138922163")).toEqual({ state: "open" });
  });

  it("closedForApplicants и текст «В архиве» без status.archived архивом не считаются", () => {
    const html = vacancyPageHtml("1", undefined, { closedForApplicants: true });
    expect(parseArchiveState(html, "1").state).toBe("unknown");
  });

  it.each([
    ["нет шаблона с данными", "<html><body>Подтвердите, что вы не робот</body></html>"],
    ["не JSON", '<template id="HH-Lux-InitialState">{oops</template>'],
    ["нет вакансии", '<template id="HH-Lux-InitialState">{"other":1}</template>'],
    ["другая вакансия", vacancyPageHtml("999", true)],
    ["archived не true", vacancyPageHtml("1", "yes" as unknown as boolean)],
  ])("%s — unknown, не архив", (_name, html) => {
    expect(parseArchiveState(html, "1").state).toBe("unknown");
  });
});

describe("pageArchiveChecker", () => {
  const response = (body: string, status = 200) => new Response(body, { status });

  it("один GET страницы вакансии на том же поддомене, без cookie", async () => {
    const fetchFn = vi.fn(async () => response(vacancyPageHtml("42", true)));
    const checker = pageArchiveChecker(() => "https://tver.hh.ru/applicant/vacancy_response?vacancyId=42", fetchFn);

    expect(await checker.check("42")).toEqual({ state: "archived" });

    expect(fetchFn).toHaveBeenCalledTimes(1);
    expect(fetchFn).toHaveBeenCalledWith("https://tver.hh.ru/vacancy/42", expect.objectContaining({ credentials: "omit" }));
  });

  it.each<[string, () => Promise<Response>, string]>([
    ["ответ 500", async () => response("", 500), "ответ 500"],
    ["ответ 404", async () => response("", 404), "ответ 404"],
    ["сеть", async () => { throw new TypeError("Failed to fetch"); }, "Failed to fetch"],
  ])("%s — unknown с причиной", async (_name, impl, error) => {
    const checker = pageArchiveChecker(() => "https://hh.ru/", vi.fn(impl));

    expect(await checker.check("1")).toEqual({ state: "unknown", error });
  });

  // Закрытая вакансия отдаёт 403 всем; отказ всем запросам — тоже 403 (#99)
  it("403 на вакансии, главная открылась — unavailable", async () => {
    const fetchFn = vi.fn(async (url: string | URL | Request) => (String(url).endsWith("/vacancy/1") ? response("", 403) : response("<html></html>")));
    const checker = pageArchiveChecker(() => "https://tver.hh.ru/applicant/vacancy_response?vacancyId=1", fetchFn);

    expect(await checker.check("1")).toEqual({ state: "unavailable" });
    expect(fetchFn).toHaveBeenLastCalledWith("https://tver.hh.ru/", expect.objectContaining({ credentials: "omit" }));
  });

  it("403 и на главной — unknown, отклик как обычно", async () => {
    const checker = pageArchiveChecker(() => "https://hh.ru/", vi.fn(async () => response("", 403)));

    expect(await checker.check("1")).toEqual({ state: "unknown", error: "ответ 403" });
  });
});
