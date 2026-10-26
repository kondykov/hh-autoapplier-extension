using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HhAutoApplier.Application.Commands.Dialogs;
using HhAutoApplier.Application.Commands.Telegram;
using HhAutoApplier.Domain.Notifications;
using HhAutoApplier.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HhAutoApplier.Tests.Api;

// Ответы работодателей (#26): приём чатов от расширения, статусы откликов, автоответ на отказ, разбор LLM и Telegram
[Collection(ApiCollection.Name)]
public class DialogsTests(ApiFactory factory) : IAsyncLifetime
{
    private static readonly DateTimeOffset Old = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly HttpClient _admin = factory.CreateApiClient();

    public async Task InitializeAsync() => await factory.ResetAsync();

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

    private static object Chat(long chatId, string vacancyId, string? state, long messageId, DateTimeOffset at, bool fromEmployer = true, string text = "Здравствуйте!") =>
        new { chatId, vacancyId, title = $"C# {vacancyId}", company = "Acme", state, messageId, text, at, fromEmployer };

    private async Task<JsonElement> Import(params object[] chats) =>
        await Json(await _admin.PostAsJsonAsync("/api/dialogs", new { chats }));

    // Отклики администратора на вакансии 1–3 отправлены
    private async Task Applied()
    {
        await Json(await _admin.PutAsJsonAsync("/api/searches", new { name = "dotnet", query = "C#" }));
        object Card(string id) => new { id, title = $"C# {id}", company = "Acme", description = "", salaryCurrency = "RUR", workFormat = "Удалённо" };
        await Json(await _admin.PostAsJsonAsync("/api/import", new { search = "dotnet", vacancies = new[] { Card("1"), Card("2"), Card("3") } }));
        foreach (var id in new[] { "1", "2", "3" })
            await Json(await _admin.PostAsJsonAsync($"/api/vacancies/{id}/status", new { status = "Applied" }));
    }

    private Task<VacancyStatus> StatusOf(string id) =>
        factory.WithDbAsync(db => db.JobApplications.Where(a => a.VacancyId == id).Select(a => a.Status).SingleAsync());

    [Fact]
    public async Task Import_SetsStatuses_AutoReplyOnlyForNewRejections()
    {
        await Applied();

        // Первый сбор: старый отказ и собеседование; свой последний ответ работодателю не сохраняется
        var first = await Import(Chat(11, "1", "DISCARD", 101, Old), Chat(12, "2", "INTERVIEW", 102, Old), Chat(13, "3", "RESPONSE", 103, Old, fromEmployer: false));
        Assert.Equal((3, 2, 1, 1), (first.GetProperty("received").GetInt32(), first.GetProperty("newMessages").GetInt32(),
            first.GetProperty("rejected").GetInt32(), first.GetProperty("invited").GetInt32()));
        Assert.Empty(first.GetProperty("autoReplies").EnumerateArray());
        Assert.Equal(VacancyStatus.Rejected, await StatusOf("1"));
        Assert.Equal(VacancyStatus.Invited, await StatusOf("2"));
        Assert.Equal(VacancyStatus.Applied, await StatusOf("3"));

        // Повтор того же сбора ничего не меняет; новый отказ после приглашения — с автоответом
        var second = await Import(Chat(11, "1", "DISCARD", 101, Old), Chat(12, "2", "DISCARD", 104, DateTimeOffset.UtcNow.AddMinutes(1)));
        Assert.Equal(1, second.GetProperty("newMessages").GetInt32());
        Assert.Equal(VacancyStatus.Rejected, await StatusOf("2"));
        var reply = Assert.Single(second.GetProperty("autoReplies").EnumerateArray());
        Assert.Equal(12, reply.GetProperty("chatId").GetInt64());

        await Json(await _admin.PostAsJsonAsync("/api/dialogs/12/auto-reply", new { sent = true }));
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.PostAsJsonAsync("/api/dialogs/12/auto-reply", new { sent = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.PostAsJsonAsync("/api/dialogs/99/auto-reply", new { sent = true })).StatusCode);

        var dialogs = await Json(await _admin.GetAsync("/api/dialogs"));
        Assert.Equal(2, dialogs.GetArrayLength());
        Assert.Equal("Sent", dialogs.EnumerateArray().Single(d => d.GetProperty("chatId").GetInt64() == 12).GetProperty("autoReply").GetString());
        // Отказ с автоответом обработан; старый отказ — нет
        Assert.Single((await Json(await _admin.GetAsync("/api/dialogs?handled=false"))).EnumerateArray());
    }

    [Fact]
    public async Task Analysis_InvitationToTelegram_ParsedOrOther()
    {
        await Applied();
        factory.Telegram.Configured = true;
        var code = (await Json(await _admin.PostAsync("/api/telegram/link", null))).GetProperty("code").GetString()!;
        await InScope(sp => sp.GetRequiredService<HandleTelegramMessageCommandHandler>()
            .Handle(new HandleTelegramMessageCommand { ChatId = 100, IsPrivate = true, Text = $"/start {code}", Username = "owner" }));

        await Import(Chat(21, "1", "INTERVIEW", 201, Old, text: "Приглашаем на звонок завтра"), Chat(22, "2", "RESPONSE", 202, Old, text: "???"), Chat(23, "3", "DISCARD", 203, Old));

        factory.Llm.Configured = true;
        factory.Llm.Answers.Enqueue("""{"kind": "invitation", "summary": "звонок завтра", "reply": "Спасибо! Удобно [время]."}""");
        factory.Llm.Answers.Enqueue("не JSON");
        var result = await InScope(sp => sp.GetRequiredService<AnalyzeDialogsCommandHandler>().Handle(new AnalyzeDialogsCommand()));
        Assert.Equal((1, 1, 0), (result.AsT0.Analyzed, result.AsT0.Failed, result.AsT0.Remaining));
        // Отказ LLM не разбирала
        Assert.Equal(2, factory.Llm.Calls);

        var dialogs = (await Json(await _admin.GetAsync("/api/dialogs"))).EnumerateArray().ToDictionary(d => d.GetProperty("chatId").GetInt64());
        Assert.Equal("Invitation", dialogs[21].GetProperty("kind").GetString());
        Assert.Equal("Спасибо! Удобно [время].", dialogs[21].GetProperty("suggestedReply").GetString());
        Assert.Equal("Other", dialogs[22].GetProperty("kind").GetString());
        Assert.Equal("Rejection", dialogs[23].GetProperty("kind").GetString());

        var notification = Assert.Single(await factory.WithDbAsync(db => db.Notifications.Where(n => n.Kind == NotificationKind.EmployerReply).ToListAsync()));
        Assert.Contains("Приглашение: C# 1", notification.Text);
        Assert.Contains("https://hh.ru/chat/21", notification.Text);
        Assert.Contains("Удобно [время]", notification.Text);

        await Json(await _admin.PostAsync("/api/dialogs/21/handled", null));
        Assert.DoesNotContain((await Json(await _admin.GetAsync("/api/dialogs?handled=false"))).EnumerateArray(), d => d.GetProperty("chatId").GetInt64() == 21);
    }
}
