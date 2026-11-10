using System.Net;
using System.Text.Json;
using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Domain.Searches;
using HhAutoApplier.Domain.Vacancies;
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

    private readonly TimeProvider _clock;

    public HhWebSource(HttpClient http, IOptions<HhOptions> options, TimeProvider clock)
    {
        _http = http;
        _clock = clock;
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
        return HhPageParser.ParseSearch(state.RootElement, search.Name, _clock.GetUtcNow()) ?? throw Blocked("на странице поиска нет выдачи");
    }

    public async Task<VacancyPageState> LoadDetailsAsync(Vacancy vacancy, CancellationToken ct)
    {
        JsonDocument? state;
        try
        {
            state = await LoadStateAsync($"vacancy/{vacancy.Id}", ct);
        }
        catch (HttpRequestException e) when (e.StatusCode >= HttpStatusCode.InternalServerError)
        {
            return VacancyPageState.TemporaryError;
        }

        if (state is null)
            return VacancyPageState.Unavailable;

        using (state)
        {
            // Чужая страница с тем же JSON (вход, мягкий антибот) — не повод закрывать отклики
            if (!HhPageParser.ParseVacancy(state.RootElement, vacancy))
                throw Blocked("на странице нет вакансии");

            return HhPageParser.IsArchived(state.RootElement) ? VacancyPageState.Archived : VacancyPageState.Available;
        }
    }

    // null — 404. Капча, отказы и страница без данных (мягкий антибот, смена вёрстки) — SourceBlockedException:
    // такой ответ ничего не говорит о вакансии, по нему нельзя закрывать отклики как «вакансия недоступна»
    private async Task<JsonDocument?> LoadStateAsync(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct);
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                return null;
            case HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests:
                throw Blocked($"ответ {(int)response.StatusCode}");
        }

        if (response.RequestMessage?.RequestUri?.AbsolutePath.Contains("captcha", StringComparison.OrdinalIgnoreCase) == true)
            throw Blocked("капча");
        response.EnsureSuccessStatusCode();
        return HhPageParser.ExtractState(await response.Content.ReadAsStringAsync(ct)) ?? throw Blocked("на странице нет данных");
    }

    private SourceBlockedException Blocked(string reason) => new(Name, reason);
}