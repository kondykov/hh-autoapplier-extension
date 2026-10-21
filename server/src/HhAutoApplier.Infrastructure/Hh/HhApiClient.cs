using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HhAutoApplier.Core.Domain;
using HhAutoApplier.Core.Rules;
using Microsoft.Extensions.Options;

namespace HhAutoApplier.Infrastructure.Hh;

public class HhOptions
{
    public const string Section = "Hh";
    // hh.ru требует осмысленный User-Agent с контактом: "HhAutoApplier/1.0 (you@example.com)"
    public string UserAgent { get; set; } = "HhAutoApplier/1.0";
    public string? AccessToken { get; set; }
    public int MaxPages { get; set; } = 3;
    public int PerPage { get; set; } = 50;
    public int RequestDelayMs { get; set; } = 400;
    public int CollectIntervalMinutes { get; set; } = 60;
    public bool CollectOnStartup { get; set; } = true;
}

public interface IVacancySource
{
    Task<IReadOnlyList<string>> SearchIdsAsync(SearchProfile search, int page, CancellationToken ct);
    Task<Vacancy?> GetAsync(string id, string searchName, CancellationToken ct);
}

public class HhApiClient : IVacancySource
{
    private readonly HttpClient _http;
    private readonly HhOptions _opt;

    public HhApiClient(HttpClient http, IOptions<HhOptions> options)
    {
        _opt = options.Value;
        _http = http;
        _http.BaseAddress = new Uri("https://api.hh.ru/");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(_opt.UserAgent);
        _http.DefaultRequestHeaders.Add("HH-User-Agent", _opt.UserAgent);
        if (!string.IsNullOrWhiteSpace(_opt.AccessToken))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _opt.AccessToken);
    }

    public async Task<IReadOnlyList<string>> SearchIdsAsync(SearchProfile search, int page, CancellationToken ct)
    {
        var url = $"vacancies?text={Uri.EscapeDataString(search.Query)}&per_page={_opt.PerPage}&page={page}&order_by=publication_time";
        if (!string.IsNullOrWhiteSpace(search.Area)) url += $"&area={Uri.EscapeDataString(search.Area)}";
        if (!string.IsNullOrWhiteSpace(search.ExtraParams)) url += "&" + search.ExtraParams.TrimStart('&');

        var result = await _http.GetFromJsonAsync<SearchResponse>(url, ct);
        return result?.Items.Select(i => i.Id).ToList() ?? [];
    }

    public async Task<Vacancy?> GetAsync(string id, string searchName, CancellationToken ct)
    {
        var response = await _http.GetAsync($"vacancies/{id}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<VacancyDto>(ct);
        return dto is null ? null : HhVacancyMapper.Map(dto, searchName);
    }
}

public static partial class HhVacancyMapper
{
    public static Vacancy Map(VacancyDto d, string searchName)
    {
        var description = HtmlToText(d.Description);
        var formats = (d.WorkFormat ?? []).Select(f => f.Name)
            .Concat(d.Schedule is { } s ? [s.Name] : []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct();
        var skills = (d.KeySkills ?? []).Select(k => k.Name).ToList();

        return new Vacancy
        {
            Id = d.Id,
            Title = d.Name,
            Company = d.Employer?.Name ?? "",
            Url = d.AlternateUrl ?? $"https://hh.ru/vacancy/{d.Id}",
            Area = d.Area?.Name ?? "",
            Description = description.Length > 4000 ? description[..4000] : description,
            SalaryFrom = d.Salary?.From,
            SalaryTo = d.Salary?.To,
            SalaryCurrency = d.Salary?.Currency,
            WorkFormat = string.Join(", ", formats),
            Stack = StackExtractor.Extract($"{d.Name} {description}", skills),
            HasTest = d.HasTest,
            PublishedAt = d.PublishedAt,
            CollectedAt = DateTimeOffset.UtcNow,
            SearchName = searchName,
        };
    }

    public static string HtmlToText(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var text = Tags().Replace(html.Replace("<li>", "\n- ").Replace("</p>", "\n").Replace("<br", "\n<br"), " ");
        return Spaces().Replace(System.Net.WebUtility.HtmlDecode(text), " ").Trim();
    }

    [GeneratedRegex("<[^>]+>")] private static partial Regex Tags();
    [GeneratedRegex(@"[ \t]+")] private static partial Regex Spaces();
}

public record SearchResponse([property: JsonPropertyName("items")] List<SearchItem> Items);
public record SearchItem([property: JsonPropertyName("id")] string Id);

public class VacancyDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("alternate_url")] public string? AlternateUrl { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("has_test")] public bool HasTest { get; set; }
    [JsonPropertyName("published_at")] public DateTimeOffset PublishedAt { get; set; }
    [JsonPropertyName("employer")] public NamedDto? Employer { get; set; }
    [JsonPropertyName("area")] public NamedDto? Area { get; set; }
    [JsonPropertyName("schedule")] public NamedDto? Schedule { get; set; }
    [JsonPropertyName("work_format")] public List<NamedDto>? WorkFormat { get; set; }
    [JsonPropertyName("key_skills")] public List<NamedDto>? KeySkills { get; set; }
    [JsonPropertyName("salary")] public SalaryDto? Salary { get; set; }
}

public class NamedDto { [JsonPropertyName("name")] public string Name { get; set; } = ""; }

public class SalaryDto
{
    [JsonPropertyName("from")] public int? From { get; set; }
    [JsonPropertyName("to")] public int? To { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
}
