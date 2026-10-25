import { describe, expect, it } from "vitest";
import { DEFAULT_RESUMES, pickProfile } from "../src/resume/profiles";

describe("pickProfile", () => {
  it("выбирает .NET по стеку", () => {
    expect(pickProfile(DEFAULT_RESUMES, "Backend C# .NET developer, ASP.NET Core")?.name).toBe(".NET");
  });
  it("выбирает PHP по стеку", () => {
    expect(pickProfile(DEFAULT_RESUMES, "PHP разработчик, Laravel")?.name).toBe("PHP");
  });
  it("null без совпадений и при ничьей", () => {
    expect(pickProfile(DEFAULT_RESUMES, "Менеджер проектов")).toBeNull();
    expect(pickProfile(DEFAULT_RESUMES, "php или c#")).toBeNull();
  });
});
