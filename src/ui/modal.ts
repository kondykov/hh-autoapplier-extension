import type { VacancyItem } from "../core/state";

export function showListModal(title: string, items: VacancyItem[]): void {
  document.getElementById("hh-autoapply-modal")?.remove();
  const overlay = document.createElement("div");
  overlay.id = "hh-autoapply-modal";
  const box = document.createElement("div");
  box.className = "box";

  const h = document.createElement("h3");
  h.textContent = `${title} (${items.length})`;
  const close = document.createElement("button");
  close.className = "hh-btn hh-btn-primary";
  close.textContent = "Закрыть";
  close.onclick = () => overlay.remove();

  const list = document.createElement("ol");
  for (const item of items) {
    const li = document.createElement("li");
    const a = document.createElement("a");
    a.href = item.link;
    a.target = "_blank";
    a.rel = "noopener";
    a.textContent = item.title;
    li.append(a, ` — ${item.company}`);
    list.append(li);
  }

  box.append(h, list, close);
  overlay.append(box);
  overlay.addEventListener("click", (e) => e.target === overlay && overlay.remove());
  document.body.append(overlay);
}
