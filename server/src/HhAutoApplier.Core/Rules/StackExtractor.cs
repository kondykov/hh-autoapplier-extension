using System.Text.RegularExpressions;

namespace HhAutoApplier.Core.Rules;

public static class TextNormalizer
{
    public static string Normalize(string? text) =>
        Regex.Replace(text ?? "", @"\s+", " ").Trim().ToLowerInvariant().Replace('ё', 'е');
}

public static class StackExtractor
{
    private static readonly (string Name, Regex Pattern)[] Dictionary = Build(new Dictionary<string, string>
    {
        ["C#"] = @"c#|c\s?sharp",
        [".NET"] = @"\.net|dotnet|asp\.net",
        ["PHP"] = @"php",
        ["Laravel"] = @"laravel",
        ["Symfony"] = @"symfony",
        ["Python"] = @"python",
        ["Java"] = @"java(?!\s?script)",
        ["Kotlin"] = @"kotlin",
        ["Go"] = @"golang|go\b",
        ["JavaScript"] = @"javascript|js\b",
        ["TypeScript"] = @"typescript|ts\b",
        ["React"] = @"react",
        ["Vue"] = @"vue|nuxt",
        ["Node.js"] = @"node\.?js",
        ["SQL"] = @"sql|postgres\w*|mysql|mssql",
        ["MongoDB"] = @"mongo\w*",
        ["Redis"] = @"redis",
        ["RabbitMQ"] = @"rabbitmq",
        ["Kafka"] = @"kafka",
        ["Docker"] = @"docker",
        ["Kubernetes"] = @"kubernetes|k8s",
        ["Git"] = @"git\b",
        ["CI/CD"] = @"ci/cd|ci\s?cd|jenkins|gitlab",
    });

    private static (string, Regex)[] Build(Dictionary<string, string> map) =>
        map.Select(kv => (kv.Key, new Regex($@"(?<![\p{{L}}\d]){kv.Value}(?![\p{{L}}\d])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200)))).ToArray();

    public static string[] Extract(string text, IEnumerable<string>? extra = null)
    {
        var found = Dictionary.Where(d => d.Pattern.IsMatch(text)).Select(d => d.Name).ToList();
        if (extra is not null)
            foreach (var e in extra.Where(x => !string.IsNullOrWhiteSpace(x)))
                if (!found.Contains(e, StringComparer.OrdinalIgnoreCase)) found.Add(e);
        return [.. found];
    }
}
