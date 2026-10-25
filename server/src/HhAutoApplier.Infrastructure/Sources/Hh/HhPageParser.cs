using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HhAutoApplier.Domain.Vacancies;

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
        try
        {
            return JsonDocument.Parse(WebUtility.HtmlDecode(html[start..end]));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // null — на странице нет выдачи (капча или другая вёрстка)
    public static IReadOnlyList<Vacancy>? ParseSearch(JsonElement state, string searchName, DateTimeOffset now)
    {
        if (!state.TryGetProperty("vacancySearchResult", out var result)
            || !result.TryGetProperty("vacancies", out var items) || items.ValueKind != JsonValueKind.Array)
            return null;

        return items.EnumerateArray()
            .Where(i => i.TryGetProperty("vacancyId", out _))
            .Select(i =>
            {
                var id = i.GetProperty("vacancyId").ToString();
                var v = Vacancy.Create(id, Str(i, "name"), $"https://hh.ru/vacancy/{id}", searchName, now);
                var hasTest = i.TryGetProperty("userTestPresent", out var t) && t.ValueKind == JsonValueKind.True;
                UpdateCard(v, i, hasTest, Date(i, "publicationTime"));
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

        UpdateCard(vacancy, v, null, Date(v, "publicationTimeIso"));

        var skills = v.TryGetProperty("keySkills", out var ks) && ks.ValueKind == JsonValueKind.Array
            ? ks.EnumerateArray().Select(k => k.ValueKind == JsonValueKind.String ? k.GetString() ?? "" : Str(k, "name"))
                .Where(s => s != "").ToList()
            : [];
        vacancy.UpdateDetails(Str(v, "name"), HtmlToText(Str(v, "description")), skills);
        return true;
    }

    // Поля, одинаковые в выдаче и на странице вакансии
    private static void UpdateCard(Vacancy vacancy, JsonElement v, bool? hasTest, DateTimeOffset? publishedAt)
    {
        var company = v.TryGetProperty("company", out var c) ? Str(c, "name") : null;
        var area = v.TryGetProperty("area", out var a) ? Str(a, "name") : null;
        var salary = v.TryGetProperty("compensation", out var comp) && comp.ValueKind == JsonValueKind.Object
            ? Salary.From(Int(comp, "from"), Int(comp, "to"), Str(comp, "currencyCode"))
            : null;
        var formats = string.Join(", ", WorkFormatCodes(v).Select(code => WorkFormats.GetValueOrDefault(code, code)).Distinct());

        vacancy.UpdateCard(company, area, salary, formats, hasTest, publishedAt);
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
                    if (c.ValueKind == JsonValueKind.String)
                        yield return c.GetString()!;
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
            ? p.GetString() ?? ""
            : "";

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n) ? n : null;

    // hh.ru отдаёт время с московским смещением, а Npgsql пишет в timestamptz только UTC
    private static DateTimeOffset? Date(JsonElement e, string name) =>
        DateTimeOffset.TryParse(Str(e, name), out var d) ? d.ToUniversalTime() : null;

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex Spaces();
}