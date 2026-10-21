import { TEXT_MARKERS, SELECTORS } from "../core/selectors";

export const qs = <T extends Element = Element>(selector: string, parent: ParentNode = document) =>
  parent.querySelector<T>(selector);

export const qsa = <T extends Element = Element>(selector: string, parent: ParentNode = document) =>
  Array.from(parent.querySelectorAll<T>(selector));

export function firstMatch(selectors: string[], parent: ParentNode = document) {
  for (const selector of selectors) {
    const el = parent.querySelector(selector);
    if (el) return { el, selector };
  }
  return null;
}

export const normalizeText = (text: string | null | undefined) =>
  (text ?? "").replace(/\u00A0/g, " ").replace(/\s+/g, " ").trim();

export function isAlreadyResponded(card: Element | null): boolean {
  if (!card) return false;
  if (card.querySelector(SELECTORS.respondedMarker)) return true;
  return normalizeText(card.textContent).includes(TEXT_MARKERS.alreadyResponded);
}
