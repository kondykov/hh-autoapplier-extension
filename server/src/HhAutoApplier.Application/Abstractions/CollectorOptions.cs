namespace HhAutoApplier.Application.Abstractions;

public class CollectorOptions
{
    public const string Section = "Collector";

    public int MaxPages { get; set; } = 3;

    // Пауза между запросами к источнику — случайная в этом диапазоне, чтобы не долбить сайт ровным ритмом
    public int DelayMinMs { get; set; } = 2000;

    public int DelayMaxMs { get; set; } = 5000;

    public int IntervalMinutes { get; set; } = 60;

    public bool CollectOnStartup { get; set; } = true;
}
