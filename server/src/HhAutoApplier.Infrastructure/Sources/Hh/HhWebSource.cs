using System.Net;
using HhAutoApplier.Core.Domain;
using Microsoft.Extensions.Options;

namespace HhAutoApplier.Infrastructure.Sources.Hh;

public class HhOptions
{
    public const string Section = "Hh";
    public bool Enabled { get; set; } = true;
    // Обычный браузерный User-Agent: публичные страницы hh.ru отдаются без входа
    public string UserAgent { get; set; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
}

// Источник hh.ru без токена и аккаунта: публичная выдача и страницы вакансий.
// hh.ru перенаправляет на региональный поддомен по IP — редиректы и cookie обрабатывает HttpClient
public class HhWebSource : IVacancySource
{
    private readonly HttpClient _http;

    public HhWebSource(HttpClient http, IOptions<HhOptions> options)
    {
        _http = http;
        _http.BaseAddress = new Uri("https://hh.ru/");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(options.Value.UserAgent);
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru-RU,ru;q=0.9");
    }

    public string Name => "hh.ru";

    public async Task<IReadOnlyList<Vacancy>> SearchAsync(SearchProfile search, int page, CancellationToken ct)
    {
        var url = $"search/vacancy?text={Uri.EscapeDataString(search.Query)}&page={page}&order_by=publication_time";
        if (!string.IsNullOrWhiteSpace(search.Area)) url += $"&area={Uri.EscapeDataString(search.Area)}";
        if (!string.IsNullOrWhiteSpace(search.ExtraParams)) url += "&" + search.ExtraParams.TrimStart('&');

        using var state = await LoadStateAsync(url, ct) ?? throw Blocked("на странице поиска нет данных");
        return HhPageParser.ParseSearch(state.RootElement, search.Name) ?? throw Blocked("на странице поиска нет выдачи");
    }

    public async Task<bool> LoadDetailsAsync(Vacancy vacancy, CancellationToken ct)
    {
        using var state = await LoadStateAsync($"vacancy/{vacancy.Id}", ct);
        return state is not null && HhPageParser.ParseVacancy(state.RootElement, vacancy);
    }

    // null — 404 или страница без данных; капча и отказы — SourceBlockedException
    private async Task<System.Text.Json.JsonDocument?> LoadStateAsync(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw Blocked($"ответ {(int)response.StatusCode}");
        if (response.RequestMessage?.RequestUri?.AbsolutePath.Contains("captcha", StringComparison.OrdinalIgnoreCase) == true)
            throw Blocked("капча");
        response.EnsureSuccessStatusCode();
        return HhPageParser.ExtractState(await response.Content.ReadAsStringAsync(ct));
    }

    private SourceBlockedException Blocked(string reason) => new(Name, reason);
}
