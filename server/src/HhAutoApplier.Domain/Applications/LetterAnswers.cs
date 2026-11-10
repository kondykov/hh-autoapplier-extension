using System.Text.RegularExpressions;

namespace HhAutoApplier.Domain.Applications;

// Блок проверенных значений в письме (#72, В7): модель их не видит, письмо с ними не переписывает, проверки #82 идут по письму без блока.
// Строится только из Answer: текст работодателя сюда не попадает. Чистые функции
public static class LetterAnswers
{
    private const string Heading = "Ответы на вопросы из вакансии:";

    // Подпись — последний абзац шаблона; длиннее — скорее обычный абзац
    private const int MaxSignature = 200;

    private const int MaxSignatureLines = 4;

    public static string Block(IEnumerable<LetterRequest> requests)
    {
        var lines = requests.Where(r => r.Answer is not null).Select(Line).Distinct().ToList();
        return lines.Count == 0 ? "" : $"{Heading}\n{string.Join("\n", lines.Select(l => $"— {l}"))}";
    }

    // Блок перед подписью шаблона, если письмо ей заканчивается: иначе подпись оказалась бы в середине
    public static string Compose(string letter, string block, string? template)
    {
        if (block == "")
            return letter;

        var body = letter.TrimEnd();
        if (Signature(template) is { } signature && body.EndsWith(signature, StringComparison.Ordinal) && body.Length > signature.Length)
            return $"{body[..^signature.Length].TrimEnd()}\n\n{block}\n\n{signature}";
        return $"{body}\n\n{block}";
    }

    // Прежний блок при обновлении значений; человек его поправил — блока не найти, письмо остаётся как есть
    public static string Strip(string letter, string block)
    {
        if (block == "")
            return letter;

        var index = letter.LastIndexOf(block, StringComparison.Ordinal);
        if (index < 0)
            return letter;

        var before = letter[..index].TrimEnd();
        var after = letter[(index + block.Length)..].TrimStart();
        return after == "" ? before : $"{before}\n\n{after}";
    }

    private static string Line(LetterRequest request) => request.Type switch
    {
        LetterRequestType.Arithmetic => $"Результат примера: {request.Value} = {request.Answer}",
        LetterRequestType.Salary => $"Желаемая зарплата на руки: {request.Answer}",
        LetterRequestType.CodeWord => $"Кодовое слово: {request.Answer}",
        _ => request.Answer!,
    };

    private static string? Signature(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
            return null;

        var paragraphs = Regex.Split(template.Replace("\r\n", "\n").Trim(), @"\n[ \t]*\n", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        if (paragraphs.Length < 2)
            return null;

        var last = paragraphs[^1].Trim();
        return last.Length <= MaxSignature && last.Count(c => c == '\n') < MaxSignatureLines ? last : null;
    }
}
