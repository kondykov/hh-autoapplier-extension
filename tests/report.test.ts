import { describe, expect, it } from "vitest";
import { toCsv } from "../src/report/applications";
import { extractSalary, extractStack, extractWorkFormat } from "../src/report/extractors";

describe("report extractors", () => {
  it("finds stack terms", () => {
    expect(extractStack("C# / ASP.NET разработчик", "PostgreSQL, Docker, REST API, JavaScript")).toEqual([
      "C#",
      "ASP.NET",
      "JavaScript",
      "PostgreSQL",
      "Docker",
      "REST",
    ]);
    expect(extractStack("Java Script и Java")).toContain("Java");
  });

  it("finds salary and format", () => {
    expect(extractSalary("Разработчик от 200 000 ₽ на руки Тверь")).toBe("от 200 000 ₽ на руки");
    expect(extractSalary("Без зарплаты")).toBe("");
    expect(extractWorkFormat("Тверь. Удалённая работа")).toBe("Удалённая");
  });

  it("builds csv with escaping and fallbacks", () => {
    const csv = toCsv([
      { title: 'A "b"', stack: "C#", company: "X", salary: "", format: "", link: "l", appliedAt: "2026-10-21T10:00:00Z" },
    ]);
    expect(csv.startsWith("\uFEFF")).toBe(true);
    expect(csv).toContain('"A ""b""";"C#";"X";"не указана";"не указан";"l";"2026-10-21"');
  });
});
