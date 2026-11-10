// Страница /vacancy/{id} hh.ru по живым 138137097 (в архиве) и 138922163 (открыта), 2026-11-10, без входа.
// JSON лежит HTML-экранированным в <template id="HH-Lux-InitialState">; status и closedForApplicants — как на живой странице, остальное сокращено
const escape = (s: string) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");

export function vacancyPageHtml(id: string, archived: boolean | undefined, extra: Record<string, unknown> = {}): string {
  const status = archived === undefined ? undefined : {
    archived, disabled: false, hiddenInArchive: false, premoderationStatus: "APPROVED", approved: true, moderatePriorityScore: 0,
  };
  const state = {
    vacancyView: {
      vacancyFull: {
        vacancy: {
          vacancyId: Number(id), name: "Senior .NET-разработчик", description: "<p>ASP.NET Core &amp; PostgreSQL</p>",
          ...(status ? { status } : {}), closedForApplicants: false, ...extra,
        },
      },
    },
  };
  const title = archived ? '<span data-qa="vacancy-title-archived-text" class="bloko-text bloko-text_small bloko-text_strong">В архиве с 4 ноября 2026</span>' : "";
  return `<!DOCTYPE html><html><head><title>Вакансия</title></head><body>
<div id="HH-React-Root"><h1 data-qa="vacancy-title">Senior .NET-разработчик</h1>${title}</div>
<template style="display:none" id="HH-Lux-InitialState">${escape(JSON.stringify(state))}</template>
</body></html>`;
}
