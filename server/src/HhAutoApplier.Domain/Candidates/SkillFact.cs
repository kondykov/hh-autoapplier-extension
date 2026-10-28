namespace HhAutoApplier.Domain.Candidates;

// Факты по навыку: где применял, сколько, что делал, цифры. Skill — название, синонимы через «/» («EF Core / Entity Framework»)
public class SkillFact
{
    public string Skill { get; init; } = "";

    public string Facts { get; init; } = "";
}
