import { describe, expect, it } from "vitest";
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
});
