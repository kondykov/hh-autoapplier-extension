using HhAutoApplier.Domain.Applications;
using HhAutoApplier.Domain.Llm;
using HhAutoApplier.Domain.Notifications;
using HhAutoApplier.Domain.Resumes;
using HhAutoApplier.Domain.Searches;
using HhAutoApplier.Domain.Telegram;
using HhAutoApplier.Domain.Users;
using HhAutoApplier.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace HhAutoApplier.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Vacancy> Vacancies { get; }

    DbSet<JobApplication> JobApplications { get; }

    DbSet<SearchProfile> Searches { get; }

    DbSet<ResumeProfile> Resumes { get; }

    DbSet<LlmCall> LlmCalls { get; }

    DbSet<ApiToken> ApiTokens { get; }

    DbSet<TelegramChat> TelegramChats { get; }

    DbSet<Notification> Notifications { get; }

    // Сброс отслеживания после неудачного сохранения (сбор продолжает со следующего поиска)
    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
