import { describe, expect, it } from "vitest";
import { EMPTY_RULES, evaluate, parseList, salaryRub } from "../src/filter/rules";

const base = { title: "Senior C# разработчик", text: "ASP.NET, Docker", stack: ["C#", ".NET"], salary: "", format: "" };

describe("filter rules", () => {
  it("passes everything with empty rules", () => {
    expect(evaluate(base, EMPTY_RULES).pass).toBe(true);
  });

  it("rejects by stop word and missing required stack", () => {
    expect(evaluate(base, { ...EMPTY_RULES, stopWords: ["senior"] }).reasons[0]).toContain("senior");
    expect(evaluate(base, { ...EMPTY_RULES, requiredStack: ["php"] }).pass).toBe(false);
    expect(evaluate(base, { ...EMPTY_RULES, requiredStack: ["php", "c#"] }).pass).toBe(true);
  });

  it("checks salary only when known and in rubles", () => {
    const rules = { ...EMPTY_RULES, minSalary: 200000 };
    expect(evaluate({ ...base, salary: "от 150 000 ₽" }, rules).pass).toBe(false);
    expect(evaluate({ ...base, salary: "250 000 ₽" }, rules).pass).toBe(true);
    expect(evaluate({ ...base, salary: "" }, rules).pass).toBe(true);
    expect(evaluate({ ...base, salary: "3000 $" }, rules).pass).toBe(true);
    expect(salaryRub("265 000 – 285 000 ₽")).toBe(285000);
  });

  it("checks format and score threshold", () => {
    expect(evaluate({ ...base, format: "офис" }, { ...EMPTY_RULES, formats: ["удалённо"] }).pass).toBe(false);
    expect(evaluate({ ...base, format: "удалённо" }, { ...EMPTY_RULES, formats: ["удалённая работа"] }).pass).toBe(true);
    const scored = { ...EMPTY_RULES, requiredStack: ["c#"], preferredStack: ["docker", "kafka"], minScore: 4 };
    expect(evaluate(base, scored)).toMatchObject({ score: 3, pass: false });
  });

  it("parses lists", () => {
    expect(parseList("php, 1с;\n стажёр ")).toEqual(["php", "1с", "стажёр"]);
  });
});
