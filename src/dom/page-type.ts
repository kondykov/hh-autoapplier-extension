export type PageType = "search" | "response" | "vacancy" | "unknown";

export function detectPageType(url: string = location.href): PageType {
  if (url.includes("/search/vacancy")) return "search";
  if (url.includes("/applicant/vacancy_response")) return "response";
  if (url.includes("/vacancy/")) return "vacancy";
  return "unknown";
}
