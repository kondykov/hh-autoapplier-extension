using System.Net;
using System.Net.Sockets;
using System.Text;
using Telegram.Bot;

namespace HhAutoApplier.Tests;

// #60: токен бота лежит в адресе запроса Bot API, но исключения Telegram.Bot — те, что идут в LogWarning(e, …)
// и в Notification.MarkFailed(e.Message), — адреса не содержат
public class TelegramTokenTests
{
    private const string Token = "123456:secret-token-value";

    private const string Secret = "secret-token-value";

    private sealed class Stub(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public Uri? Requested { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requested = request.RequestUri;
            return respond(ct);
        }
    }

    private static async Task AssertNoToken(Stub stub, TimeSpan? timeout = null)
    {
        using var http = new HttpClient(stub);
        if (timeout is { } limit)
            http.Timeout = limit;

        var error = await Assert.ThrowsAnyAsync<Exception>(() => new TelegramBotClient(Token, http).GetMe());

        // Запрос ушёл с токеном в адресе — проверка не пустая
        Assert.Contains(Secret, stub.Requested!.ToString());
        Assert.DoesNotContain(Secret, error.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"ok":false,"error_code":401,"description":"Unauthorized"}""", "application/json")]
    [InlineData(HttpStatusCode.Conflict, """{"ok":false,"error_code":409,"description":"Conflict"}""", "application/json")]
    [InlineData(HttpStatusCode.BadGateway, "<html>Bad Gateway</html>", "text/html")]
    public Task BotApiError_NoTokenInException(HttpStatusCode status, string body, string type) =>
        AssertNoToken(new Stub(_ => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, type) })));

    [Fact]
    public Task Timeout_NoTokenInException() =>
        AssertNoToken(new Stub(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        }), TimeSpan.FromMilliseconds(50));

    // Настоящий сетевой стек: соединение отклонено на закрытом порту
    [Fact]
    public async Task ConnectionRefused_NoTokenInException()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using var http = new HttpClient();
        var client = new TelegramBotClient(new TelegramBotClientOptions(Token, $"http://127.0.0.1:{port}"), http);

        var error = await Assert.ThrowsAnyAsync<Exception>(() => client.GetMe());

        Assert.DoesNotContain(Secret, error.ToString());
    }
}
