using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HhAutoApplier.Core.Domain;
using HhAutoApplier.Core.Rules;

namespace HhAutoApplier.Infrastructure.Sources.Hh;

// Разбор публичных страниц hh.ru: данные лежат JSON-ом в <template id="HH-Lux-InitialState">.
// Единственное место, которое знает структуру страниц hh.ru — при смене вёрстки правится только он
public static partial class HhPageParser
{
    private const string StateMarker = "id=\"HH-Lux-InitialState\"";

    private static readonly Dictionary<string, string> WorkFormats = new()
    {
        ["ON_SITE"] = "На месте работодателя",
        ["REMOTE"] = "Удалённо",
        ["HYBRID"] = "Гибрид",
        ["FIELD_WORK"] = "Разъездной",
    };

    public static JsonDocument? ExtractState(string html)
    {
        var tag = html.IndexOf(StateMarker, StringComparison.Ordinal);
        if (tag < 0) return null;
        var start = html.IndexOf('>', tag) + 1;
        var end = html.IndexOf("</", start, StringComparison.Ordinal);
        if (start <= 0 || end < 0) return null;
        try { return JsonDocument.Parse(WebUtility.HtmlDecode(html[start..end])); }
        catch (JsonException) { return null; }
    }

    // null — на странице нет выдачи (капча или другая вёрстка)
    public static IReadOnlyList<Vacancy>? ParseSearch(JsonElement state, string searchName)
    {
        if (!state.TryGetProperty("vacancySearchResult", out var result)
            || !result.TryGetProperty("vacancies", out var items) || items.ValueKind != JsonValueKind.Array)
            return null;

        var now = DateTimeOffset.UtcNow;
        return items.EnumerateArray()
            .Where(i => i.TryGetProperty("vacancyId", out _))
            .Select(i =>
            {
                var id = i.GetProperty("vacancyId").ToString();
                var v = new Vacancy
                {
                    Id = id,
                    Title = Str(i, "name"),
                    Url = $"https://hh.ru/vacancy/{id}",
                    HasTest = i.TryGetProperty("userTestPresent", out var t) && t.ValueKind == JsonValueKind.True,
                    PublishedAt = Date(i, "publicationTime") ?? now,
                    CollectedAt = now,
                    SearchName = searchName,
                };
                FillCommon(v, i);
                return v;
            })
            .ToList();
    }

    // false — на странице нет вакансии
    public static bool ParseVacancy(JsonElement state, Vacancy vacancy)
    {
        if (!state.TryGetProperty("vacancyView", out var view)
            || !view.TryGetProperty("vacancyFull", out var full)
            || !full.TryGetProperty("vacancy", out var v))
            return false;

        if (Str(v, "name") is { Length: > 0 } title) vacancy.Title = title;
        FillCommon(vacancy, v);
        if (Date(v, "publicationTimeIso") is { } published) vacancy.PublishedAt = published;

        var description = HtmlToText(Str(v, "description"));
        vacancy.Description = description.Length > 4000 ? description[..4000] : description;
        var skills = v.TryGetProperty("keySkills", out var ks) && ks.ValueKind == JsonValueKind.Array
            ? ks.EnumerateArray().Select(k => k.ValueKind == JsonValueKind.String ? k.GetString() ?? "" : Str(k, "name"))
                .Where(s => s != "").ToList()
            : [];
        vacancy.Stack = StackExtractor.Extract($"{vacancy.Title} {description}", skills);
        return true;
    }

    // Поля, одинаковые в выдаче и на странице вакансии
    private static void FillCommon(Vacancy vacancy, JsonElement v)
    {
        if (v.TryGetProperty("company", out var company) && Str(company, "name") is { Length: > 0 } name) vacancy.Company = name;
        if (v.TryGetProperty("area", out var area) && Str(area, "name") is { Length: > 0 } areaName) vacancy.Area = areaName;
        if (v.TryGetProperty("compensation", out var comp) && comp.ValueKind == JsonValueKind.Object)
        {
            vacancy.SalaryFrom = Int(comp, "from");
            vacancy.SalaryTo = Int(comp, "to");
            vacancy.SalaryCurrency = Str(comp, "currencyCode") is { Length: > 0 } cur ? cur : null;
        }
        var formats = WorkFormatCodes(v).Select(c => WorkFormats.GetValueOrDefault(c, c)).Distinct().ToList();
        if (formats.Count > 0) vacancy.WorkFormat = string.Join(", ", formats);
    }

    // В выдаче: [{"workFormatsElement":["REMOTE"]}], на странице вакансии: ["REMOTE"]
    private static IEnumerable<string> WorkFormatCodes(JsonElement v)
    {
        if (!v.TryGetProperty("workFormats", out var wf) || wf.ValueKind != JsonValueKind.Array) yield break;
        foreach (var e in wf.EnumerateArray())
        {
            if (e.ValueKind == JsonValueKind.String) yield return e.GetString()!;
            else if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("workFormatsElement", out var inner)
                     && inner.ValueKind == JsonValueKind.Array)
                foreach (var c in inner.EnumerateArray())
                    if (c.ValueKind == JsonValueKind.String) yield return c.GetString()!;
        }
    }

    public static string HtmlToText(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var text = Tags().Replace(html.Replace("<li>", "\n- ").Replace("</p>", "\n").Replace("<br", "\n<br"), " ");
        return Spaces().Replace(WebUtility.HtmlDecode(text), " ").Trim();
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? "" : "";

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n) ? n : null;

    private static DateTimeOffset? Date(JsonElement e, string name) =>
        DateTimeOffset.TryParse(Str(e, name), out var d) ? d : null;

    [GeneratedRegex("<[^>]+>")] private static partial Regex Tags();
    [GeneratedRegex(@"[ \t]+")] private static partial Regex Spaces();
}
