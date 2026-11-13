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
            state = await LoadStateAsync($"vacancy/{vacancy.Id}", ct, vacancyPage: true);
        }
        catch (HttpRequestException e) when (e.StatusCode >= HttpStatusCode.InternalServerError)
        {
            return VacancyPageState.TemporaryError;
        }
        catch (ForbiddenVacancyException)
        {
            // Закрытая вакансия (скрыта, снята модерацией) отдаёт 403 всем, со входом и без (139119374, 2026-11-13, #99).
            // Отказ всему сбору — тоже 403: открылась главная — закрыта именно эта вакансия, иначе блокировка
            if (!await HomeOpensAsync(ct))
                throw Blocked("ответ 403");
            return VacancyPageState.Unavailable;
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
    private async Task<JsonDocument?> LoadStateAsync(string url, CancellationToken ct, bool vacancyPage = false)
    {
        using var response = await _http.GetAsync(url, ct);
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                return null;
            case HttpStatusCode.Forbidden when vacancyPage:
                throw new ForbiddenVacancyException();
            case HttpStatusCode.Forbidden:
            case HttpStatusCode.TooManyRequests:
                throw Blocked($"ответ {(int)response.StatusCode}");
        }

        if (IsCaptcha(response))
            throw Blocked("капча");
        response.EnsureSuccessStatusCode();
        return HhPageParser.ExtractState(await response.Content.ReadAsStringAsync(ct)) ?? throw Blocked("на странице нет данных");
    }

    // Контрольный запрос после 403 на странице вакансии: главная с данными страницы, без капчи
    private async Task<bool> HomeOpensAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync("", ct);
        if (response.StatusCode != HttpStatusCode.OK || IsCaptcha(response))
            return false;

        using var state = HhPageParser.ExtractState(await response.Content.ReadAsStringAsync(ct));
        return state is not null;
    }

    private static bool IsCaptcha(HttpResponseMessage response) =>
        response.RequestMessage?.RequestUri?.AbsolutePath.Contains("captcha", StringComparison.OrdinalIgnoreCase) == true;

    private SourceBlockedException Blocked(string reason) => new(Name, reason);

    // 403 на странице вакансии: блокировка или закрытая вакансия — решает контрольный запрос в LoadDetailsAsync
    private sealed class ForbiddenVacancyException : Exception;
}