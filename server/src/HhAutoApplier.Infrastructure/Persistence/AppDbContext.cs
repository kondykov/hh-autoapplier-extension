using HhAutoApplier.Application.Abstractions;
using HhAutoApplier.Domain.Applications;
using HhAutoApplier.Domain.Candidates;
using HhAutoApplier.Domain.Dialogs;
using HhAutoApplier.Domain.Llm;
using HhAutoApplier.Domain.Resumes;
using HhAutoApplier.Domain.Notifications;
using HhAutoApplier.Domain.Searches;
using HhAutoApplier.Domain.Telegram;
using HhAutoApplier.Domain.Users;
using HhAutoApplier.Domain.Vacancies;
using HhAutoApplier.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HhAutoApplier.Infrastructure.Persistence;

// Таблицы Identity (AspNetUsers, AspNetRoles и связи) — в той же базе
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options), IAppDbContext
{
    public DbSet<Vacancy> Vacancies => Set<Vacancy>();

    public DbSet<JobApplication> JobApplications => Set<JobApplication>();

    public DbSet<SearchProfile> Searches => Set<SearchProfile>();

    public DbSet<ResumeProfile> Resumes => Set<ResumeProfile>();

    public DbSet<CandidateProfile> CandidateProfiles => Set<CandidateProfile>();

    public DbSet<LlmCall> LlmCalls => Set<LlmCall>();

    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();

    public DbSet<TelegramChat> TelegramChats => Set<TelegramChat>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<EmployerMessage> EmployerMessages => Set<EmployerMessage>();

    // Маппинг каждой сущности — в своём классе в Configurations/; схема Identity — из базового класса
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}