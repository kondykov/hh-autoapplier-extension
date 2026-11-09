using HhAutoApplier.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace HhAutoApplier.Infrastructure.Telegram;

// Клиент Bot API (Telegram.Bot) один на приложение; без токена Client = null
public class TelegramBot : ITelegramBot
{
    private readonly ILogger<TelegramBot> _logger;

    private string? _username;

    public TelegramBot(IOptions<TelegramOptions> options, IHttpClientFactory http, ILogger<TelegramBot> logger)
    {
        _logger = logger;
        if (!string.IsNullOrWhiteSpace(options.Value.BotToken))
            Client = new TelegramBotClient(options.Value.BotToken.Trim(), http.CreateClient(nameof(TelegramBot)));
    }

    public ITelegramBotClient? Client { get; }

    public bool IsConfigured => Client is not null;

    public async Task<string?> GetUsernameAsync(CancellationToken ct = default)
    {
        if (_username is not null || Client is null)
            return _username;

        try
        {
            _username = (await Client.GetMe(ct)).Username;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogWarning(e, "Telegram: имя бота не получено");
        }

        return _username;
    }
}
