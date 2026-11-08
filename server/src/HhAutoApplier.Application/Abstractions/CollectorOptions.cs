namespace HhAutoApplier.Application.Abstractions;

public class CollectorOptions
{
    public const string Section = "Collector";

    // Страниц выдачи на поиск за проход (по 50 вакансий): обычно выдача кончается раньше
    public int MaxPages { get; set; } = 20;

    // Пауза между запросами к источнику — случайная в этом диапазоне, чтобы не долбить сайт ровным ритмом
    public int DelayMinMs { get; set; } = 2000;

    public int DelayMaxMs { get; set; } = 5000;

    public int IntervalMinutes { get; set; } = 60;

    public bool CollectOnStartup { get; set; } = true;
}
