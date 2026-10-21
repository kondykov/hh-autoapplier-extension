namespace HhAutoApplier.Core.Domain;

public enum VacancyStatus
{
    New = 0,
    Queued = 1,
    Applied = 2,
    Skipped = 3,
    Rejected = 4,
    Failed = 5,
}

public class Vacancy
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public string Company { get; set; } = "";
    public string Url { get; set; } = "";
    public string Area { get; set; } = "";
    public string Description { get; set; } = "";
    public int? SalaryFrom { get; set; }
    public int? SalaryTo { get; set; }
    public string? SalaryCurrency { get; set; }
    public string WorkFormat { get; set; } = "";
    public string[] Stack { get; set; } = [];
    public bool HasTest { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset CollectedAt { get; set; }
    public string SearchName { get; set; } = "";

    public VacancyStatus Status { get; set; }
    public int Score { get; set; }
    public string? StatusReason { get; set; }
    public string? ResumeProfile { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
}
