import { DELAYS, pause } from "../core/delays";
import { SELECTORS } from "../core/selectors";
import { qs, qsa } from "../dom/query";
import type { Logger } from "../infra/logger";
import { isRejection } from "./matcher";
import { humanClick } from "../dom/input";

const processed = new WeakSet<Element>();

export async function enableUnreadFilter(): Promise<void> {
  const filter = qs<HTMLElement>(SELECTORS.chatUnreadFilter);
  if (filter && filter.getAttribute("aria-checked") !== "true") await humanClick(filter);
}

// Открывает по одному ещё необработанный чат с отказом; возвращает false, если таких не осталось
export async function openNextRejection(logger: Logger): Promise<boolean> {
  await enableUnreadFilter();
  const badge = qsa<HTMLElement>(SELECTORS.chatUnreadBadge).find(
    (b) => !processed.has(b) && isRejection(b.textContent ?? ""),
  );
  if (!badge) return false;
  processed.add(badge);
  badge.scrollIntoView?.({ block: "center" });
  await humanClick(badge);
  logger.log(`Отказ открыт: "${(badge.textContent ?? "").trim().slice(0, 60)}"`, "step");
  await pause(DELAYS.step);
  return true;
}
