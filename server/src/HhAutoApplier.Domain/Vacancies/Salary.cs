namespace HhAutoApplier.Domain.Vacancies;

// Вилка зарплаты; без валюты считается рублями
public sealed record Salary(int? Min, int? Max, string Currency)
{
    private static readonly IReadOnlyDictionary<string, decimal> RubRates = new Dictionary<string, decimal>
    {
        ["RUR"] = 1, ["RUB"] = 1, ["USD"] = 90, ["EUR"] = 100, ["KZT"] = 0.18m, ["BYR"] = 30, ["BYN"] = 30,
    };

    public static Salary? From(int? min, int? max, string? currency)
    {
        if (min is null && max is null)
            return null;

        return new Salary(min, max, string.IsNullOrWhiteSpace(currency) ? "RUR" : currency.ToUpperInvariant());
    }

    // Верхняя граница вилки (или нижняя, если верхней нет) в рублях; null — неизвестная валюта
    public int? ToRub()
    {
        var amount = Max ?? Min;
        if (amount is null)
            return null;

        return RubRates.TryGetValue(Currency, out var rate) ? (int)(amount.Value * rate) : null;
    }

    public override string ToString() =>
        $"{(Min is null ? "" : $"от {Min} ")}{(Max is null ? "" : $"до {Max} ")}{Currency}".Trim();
}
