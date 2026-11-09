using System.Text.RegularExpressions;
using HhAutoApplier.Domain;
using HhAutoApplier.Domain.Notifications;
using HhAutoApplier.Domain.Telegram;

namespace HhAutoApplier.Tests;

public class TelegramDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Code_FitsStartLink_StoredAsHash_ExpiresAfter15Minutes()
    {
        var chat = TelegramChat.Create(Guid.NewGuid());
        var code = chat.IssueCode(Now);

        // Параметр start в ссылке t.me: только A-Z, a-z, 0-9, _ и -, до 64 символов
        Assert.Matches(new Regex("^[A-Za-z0-9_-]{1,64}$"), code);
        Assert.Equal(TelegramChat.HashOf(code), chat.CodeHash);
        Assert.DoesNotContain(code, chat.CodeHash);

        Assert.Throws<DomainException>(() => chat.Link(100, "owner", Now.AddMinutes(16)));
        Assert.False(chat.Linked);

        chat.Link(100, " owner ", Now.AddMinutes(14));
        Assert.Equal((100L, "owner"), (chat.ChatId!.Value, chat.Username));
        Assert.Null(chat.CodeHash);

        // Код использован — второй раз не привязать
        Assert.Throws<DomainException>(() => chat.Link(200, null, Now.AddMinutes(14)));
    }

    [Fact]
    public void NewCode_KeepsLinkedChat_UntilNewChatLinks()
    {
        var chat = TelegramChat.Create(Guid.NewGuid());
        chat.IssueCode(Now);
        chat.Link(100, null, Now);

        var first = chat.IssueCode(Now);
        var second = chat.IssueCode(Now);
        Assert.NotEqual(TelegramChat.HashOf(first), chat.CodeHash);
        Assert.Equal(100, chat.ChatId);

        chat.Link(200, null, Now);
        Assert.Equal(200, chat.ChatId);
        Assert.Equal(TelegramChat.HashOf(second), TelegramChat.HashOf($" {second} "));

        chat.Unlink();
        Assert.False(chat.Linked);
    }

    [Fact]
    public void Notification_TrimmedToTelegramLimit_StopsAfterAttempts()
    {
        var n = Notification.Create(Guid.NewGuid(), NotificationKind.Review, "review:1", new string('a', 5000), Now, "1");
        Assert.Equal(Notification.MaxTextLength, n.Text.Length);
        Assert.Throws<DomainException>(() => Notification.Create(Guid.NewGuid(), NotificationKind.Captcha, "k", " ", Now));

        for (var i = 0; i < Notification.MaxAttempts; i++)
        {
            Assert.True(n.Pending);
            n.MarkFailed("429");
        }

        Assert.False(n.Pending);

        var cancelled = Notification.Create(Guid.NewGuid(), NotificationKind.Captcha, "k", "капча", Now);
        cancelled.Cancel("чат отвязан");
        Assert.False(cancelled.Pending);
    }
}
