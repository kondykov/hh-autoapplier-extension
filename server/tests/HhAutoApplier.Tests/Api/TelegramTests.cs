using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HhAutoApplier.Application.Commands.Notifications;
using HhAutoApplier.Application.Commands.Telegram;
using HhAutoApplier.Application.Dtos;
using HhAutoApplier.Application.Notifications;
using HhAutoApplier.Domain.Notifications;
using HhAutoApplier.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HhAutoApplier.Tests.Api;

// Telegram (#28): привязка чата кодом, команды и кнопки бота, уведомления в outbox. Транспорт Telegram не участвует:
// сообщения и нажатия подаются прямо в обработчики, как их подаёт TelegramHostedService
[Collection(ApiCollection.Name)]
public class TelegramTests(ApiFactory factory) : IAsyncLifetime
{
    private const long OwnerChat = 100;

    private const long UserChat = 200;

    private const string UserPassword = "user-password-1";

    private readonly HttpClient _admin = factory.CreateApiClient();

    public async Task InitializeAsync()
    {
        await factory.ResetAsync();
        factory.Telegram.Configured = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private Task<IReadOnlyList<BotMessage>> Send(long chat, string text, bool isPrivate = true) =>
        InScope(sp => sp.GetRequiredService<HandleTelegramMessageCommandHandler>()
            .Handle(new HandleTelegramMessageCommand { ChatId = chat, IsPrivate = isPrivate, Text = text, Username = "owner" }));

    private Task<BotCallbackResult> Press(long chat, string data) =>
        InScope(sp => sp.GetRequiredService<HandleTelegramCallbackCommandHandler>().Handle(new HandleTelegramCallbackCommand { ChatId = chat, Data = data }));

    private Task<int> NotifyReviews() => InScope(sp => sp.GetRequiredService<NotifyReviewsCommandHandler>().Handle(new NotifyReviewsCommand()));

    private Task<List<Notification>> Notifications() => factory.WithDbAsync(db => db.Notifications.OrderBy(n => n.Id).ToListAsync());

    private static async Task<string> Code(HttpClient client) =>
        (await Json(await client.PostAsync("/api/telegram/link", null))).GetProperty("code").GetString()!;

    private async Task Link(HttpClient client, long chat) => await Send(chat, $"/start {await Code(client)}");

    private async Task<HttpClient> CreateUser(string email)
    {
        await Json(await _admin.PostAsJsonAsync("/api/users", new { email, password = UserPassword, role = "User" }));
        return factory.CreateLoggedInClient(email, UserPassword);
    }

    // Сомнительные отклики администратора: 1…count ждут ручной проверки
    private async Task CreateReviews(int count = 2)
    {
        factory.Llm.Configured = true;
        await Json(await _admin.PutAsJsonAsync("/api/searches", new { name = "dotnet", query = "C#" }));
        object Card(string id) => new { id, title = $"C# разработчик {id}", company = "Acme", description = "", salaryCurrency = "RUR", workFormat = "Удалённо" };
        await Json(await _admin.PostAsJsonAsync("/api/import", new { search = "dotnet", vacancies = Enumerable.Range(1, count).Select(i => Card($"{i}")).ToArray() }));
        for (var i = 0; i < count; i++)
            factory.Llm.Answers.Enqueue("""{"verdict": "doubtful", "score": 55, "resume": ".NET", "reason": "мало опыта"}""");
        await Json(await _admin.PostAsJsonAsync("/api/llm/evaluate", new { limit = 10 }));
    }

    private Task<VacancyStatus> StatusOf(string id) =>
        factory.WithDbAsync(db => db.JobApplications.Where(a => a.VacancyId == id).Select(a => a.Status).SingleAsync());

    [Fact]
    public async Task Link_AfterPasswordLogin_CodeOnce_Unlink()
    {
        factory.Telegram.Configured = false;
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.PostAsync("/api/telegram/link", null)).StatusCode);
        factory.Telegram.Configured = true;

        // Токеном код не получить: чат получает право решать за пользователя
        Assert.Equal(HttpStatusCode.Conflict, (await factory.CreateTokenClient().PostAsync("/api/telegram/link", null)).StatusCode);

        var link = await Json(await _admin.PostAsync("/api/telegram/link", null));
        var code = link.GetProperty("code").GetString()!;
        Assert.Equal($"https://t.me/test_bot?start={code}", link.GetProperty("link").GetString());
        Assert.False((await Json(await _admin.GetAsync("/api/telegram"))).GetProperty("linked").GetBoolean());

        var reply = Assert.Single(await Send(OwnerChat, $"/start {code}"));
        Assert.Contains(ApiFactory.AdminEmail, reply.Text);
        var state = await Json(await _admin.GetAsync("/api/telegram"));
        Assert.True(state.GetProperty("linked").GetBoolean());
        Assert.Equal("owner", state.GetProperty("username").GetString());

        // Код одноразовый
        Assert.Contains("не найден", Assert.Single(await Send(UserChat, $"/start {code}")).Text);

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync("/api/telegram")).StatusCode);
        Assert.False((await Json(await _admin.GetAsync("/api/telegram"))).GetProperty("linked").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.DeleteAsync("/api/telegram")).StatusCode);
        Assert.Equal(BotTexts.NotLinked, Assert.Single(await Send(OwnerChat, "/status")).Text);
    }

    [Fact]
    public async Task Chat_LinkedToOneUser_RelinkMovesIt()
    {
        var user = await CreateUser("user@test.local");
        await Link(_admin, OwnerChat);
        await Link(user, OwnerChat);

        // Чат перешёл к пользователю; у администратора привязки больше нет
        Assert.False((await Json(await _admin.GetAsync("/api/telegram"))).GetProperty("linked").GetBoolean());
        Assert.True((await Json(await user.GetAsync("/api/telegram"))).GetProperty("linked").GetBoolean());
    }

    [Fact]
    public async Task Commands_OnlyInLinkedPrivateChat()
    {
        await CreateReviews();
        Assert.Equal(BotTexts.NotLinked, Assert.Single(await Send(OwnerChat, "/status")).Text);

        await Link(_admin, OwnerChat);
        Assert.Empty(await Send(OwnerChat, "/status", isPrivate: false));
        Assert.Contains("На проверке: 2", Assert.Single(await Send(OwnerChat, "/status@test_bot")).Text);
        Assert.Contains("/review", Assert.Single(await Send(OwnerChat, "привет")).Text);

        // Отвязка — только подтверждением
        var confirm = Assert.Single(await Send(OwnerChat, "/stop"));
        Assert.Equal(["unlink:", "cancel:"], confirm.Buttons.Select(b => b.Data));
        Assert.Equal("Отменено", (await Press(OwnerChat, "cancel:")).Status);
        Assert.Contains("На проверке: 2", Assert.Single(await Send(OwnerChat, "/status")).Text);

        var unlinked = await Press(OwnerChat, "unlink:");
        Assert.Contains("отвязан", unlinked.Status);
        Assert.Equal(BotMenu.Remove, Assert.Single(unlinked.Replies).Menu);
        Assert.Equal(BotTexts.NotLinked, Assert.Single(await Send(OwnerChat, "/status")).Text);
    }

    [Fact]
    public async Task Buttons_MenuStatusAndReview()
    {
        await Link(_admin, OwnerChat);
        Assert.Equal(BotMenu.Show, Assert.Single(await Send(OwnerChat, BotTexts.HelpButton)).Menu);
        // Проверять нечего — у статуса меню, без кнопки
        var empty = Assert.Single(await Send(OwnerChat, BotTexts.StatusButton));
        Assert.Empty(empty.Buttons);
        Assert.Equal(BotMenu.Show, empty.Menu);

        await CreateReviews();
        var status = Assert.Single(await Send(OwnerChat, BotTexts.StatusButton));
        Assert.Contains("На проверке: 2", status.Text);
        var show = Assert.Single(status.Buttons);
        Assert.Contains("(2)", show.Text);

        // Кнопка «На проверке» — как /review: сообщения с кнопками решения, исходное не меняется
        var pressed = await Press(OwnerChat, show.Data);
        Assert.Null(pressed.Status);
        Assert.Equal(2, pressed.Replies.Count);
        Assert.All(pressed.Replies, r => Assert.Equal(2, r.Buttons.Count));
        Assert.Equal(2, (await Send(OwnerChat, BotTexts.ReviewButton)).Count);

        Assert.Equal("Неизвестная кнопка", (await Press(OwnerChat, "nonsense")).Notice);
        Assert.Equal("Чат не привязан", (await Press(UserChat, show.Data)).Notice);
    }

    [Fact]
    public void NotificationButtons_ByKind()
    {
        Assert.Equal(["approve:7", "decline:7"], BotTexts.NotificationButtons(NotificationKind.Review, "review:1", "7").Select(b => b.Data));
        Assert.Equal(["review:"], BotTexts.NotificationButtons(NotificationKind.Review, "review-digest:5", null).Select(b => b.Data));
        Assert.Equal(["handled:21"], BotTexts.NotificationButtons(NotificationKind.EmployerReply, "dialog:21:201", null).Select(b => b.Data));
        Assert.Empty(BotTexts.NotificationButtons(NotificationKind.Captcha, "captcha:1", null));
    }

    [Fact]
    public async Task Review_ApproveAndDeclineFromChat_OnlyOwnApplications()
    {
        await CreateReviews();
        await Link(_admin, OwnerChat);

        var messages = await Send(OwnerChat, "/review");
        Assert.Equal(2, messages.Count);
        Assert.All(messages, m => Assert.Equal(["approve", "decline"], m.Buttons.Select(b => b.Data.Split(':')[0])));
        Assert.Contains("мало опыта", messages[0].Text);

        // Чужой чат не решает за владельца
        await Link(await CreateUser("user@test.local"), UserChat);
        Assert.Equal("Вакансия не найдена", (await Press(UserChat, "approve:1")).Notice);
        Assert.Equal(VacancyStatus.Review, await StatusOf("1"));

        Assert.Equal("Одобрено", (await Press(OwnerChat, "approve:1")).Notice);
        Assert.Equal(VacancyStatus.Queued, await StatusOf("1"));
        Assert.Equal("Уже решено", (await Press(OwnerChat, "decline:1")).Notice);

        Assert.Equal("❌ Отклонено", (await Press(OwnerChat, "decline:2")).Status);
        Assert.Equal(VacancyStatus.Skipped, await StatusOf("2"));
        Assert.Equal("Неизвестная кнопка", (await Press(OwnerChat, "delete:2")).Notice);
        Assert.Equal("Откликов на проверке нет", Assert.Single(await Send(OwnerChat, "/review")).Text);
    }

    [Fact]
    public async Task ReviewNotifications_OncePerApplication_OnlyToLinkedChat()
    {
        await CreateReviews();
        Assert.Equal(0, await NotifyReviews());

        await Link(_admin, OwnerChat);
        Assert.Equal(2, await NotifyReviews());
        Assert.Equal(0, await NotifyReviews());

        var sent = await Notifications();
        Assert.All(sent, n => Assert.Equal(NotificationKind.Review, n.Kind));
        Assert.Equal(["1", "2"], sent.Select(n => n.VacancyId).Order());
        Assert.All(sent, n => Assert.Null(n.SentAt));
    }

    [Fact]
    public async Task ReviewNotifications_ManyAtOnce_OneDigest_NotRepeatedOneByOne()
    {
        await Link(_admin, OwnerChat);
        await CreateReviews(5);

        Assert.Equal(1, await NotifyReviews());
        Assert.Equal(0, await NotifyReviews());

        var sent = await Notifications();
        var digest = Assert.Single(sent, n => n.Attempts < Notification.MaxAttempts);
        Assert.Contains("На проверке новых: 5 (всего 5)", digest.Text);
        Assert.Contains("C# разработчик 3", digest.Text);
        Assert.Contains("/review", digest.Text);
        Assert.Null(digest.VacancyId);
        // Отклики сводки по одному не придут: их уведомления сразу отменены
        Assert.Equal(5, sent.Count(n => !n.Pending && n.Key.StartsWith("review:")));
    }

    [Fact]
    public async Task ExtensionEvents_ToOwner_Deduplicated()
    {
        var key = factory.CreateTokenClient();
        var captcha = new { kind = "Captcha", message = "vacancy 1" };

        // Чат не привязан — доставить некому
        Assert.False((await Json(await key.PostAsJsonAsync("/api/events", captcha))).GetProperty("notified").GetBoolean());

        await Link(_admin, OwnerChat);
        Assert.True((await Json(await key.PostAsJsonAsync("/api/events", captcha))).GetProperty("notified").GetBoolean());
        Assert.False((await Json(await key.PostAsJsonAsync("/api/events", captcha))).GetProperty("notified").GetBoolean());
        Assert.True((await Json(await key.PostAsJsonAsync("/api/events", new { kind = "DailyLimit" }))).GetProperty("notified").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await key.PostAsJsonAsync("/api/events", new { kind = "Unknown" })).StatusCode);

        var sent = await Notifications();
        Assert.Equal([NotificationKind.Captcha, NotificationKind.DailyLimit], sent.Select(n => n.Kind));
        Assert.Contains("vacancy 1", sent[0].Text);

        factory.Telegram.Configured = false;
        Assert.False((await Json(await key.PostAsJsonAsync("/api/events", new { kind = "Captcha" }))).GetProperty("notified").GetBoolean());
    }

    [Fact]
    public async Task ServerFailures_OnlyToAdmins()
    {
        await Link(_admin, OwnerChat);
        await Link(await CreateUser("user@test.local"), UserChat);

        await InScope(async sp =>
        {
            await sp.GetRequiredService<Notifier>().NotifyAdminsAsync(NotificationKind.SourceBlocked, "source-blocked:test", "капча");
            return 0;
        });

        var sent = Assert.Single(await Notifications());
        var adminId = await factory.WithDbAsync(db => db.Users.Where(u => u.Email == ApiFactory.AdminEmail).Select(u => u.Id).SingleAsync());
        Assert.Equal(adminId, sent.UserId);
    }
}
