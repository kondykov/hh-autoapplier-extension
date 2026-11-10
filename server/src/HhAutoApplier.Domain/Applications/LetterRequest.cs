using HhAutoApplier.Domain.Candidates;
using HhAutoApplier.Domain.Services;

namespace HhAutoApplier.Domain.Applications;

// Что работодатель просит написать в письме (#72). Прочее — ссылки, контакты, внешние действия: сервер это не выполняет, решает человек
public enum LetterRequestType
{
    Arithmetic = 0,
    Salary = 1,
    TestTask = 2,
    CodeWord = 3,
    Other = 4,
}

// Состояние просьб отклика. NotExtracted — оценки ещё не было или отклик старый; None — просьб нет;
// Found — есть незакрытые; Closed — все закрыты письмом или решением человека; Failed — ответ модели не разобран, просьбы неизвестны
public enum LetterRequestsState
{
    NotExtracted = 0,
    None = 1,
    Found = 2,
    Closed = 3,
    Failed = 4,
}

// Просьба работодателя к письму; хранится в отклике JSON-ом. Text — цитата только для показа человеку: в письмо идёт лишь Answer.
// Answer появляется из проверенного источника: калькулятор, профиль кандидата, подтверждение человеком
public class LetterRequest
{
    public const int MaxText = 300;

    public const int MaxCodeWord = 30;

    public LetterRequestType Type { get; init; }

    public string Text { get; init; } = "";

    // Выражение в нормальной записи или кодовое слово; null — не прошло проверку или у типа значения нет
    public string? Value { get; init; }

    public string? Answer { get; init; }

    // Значение дописано в письмо или человек решил просьбу сам
    public bool Closed { get; init; }

    // Модель выделила значение из текста вакансии, но верим ему, только если оно дословно там есть и проходит проверку типа.
    // Значение модели для зарплаты и тестового не читается: они только из профиля кандидата
    public static LetterRequest Extracted(LetterRequestType type, string? text, string? value, string vacancyText)
    {
        var quote = Collapse(text ?? "");
        var source = Collapse(vacancyText);
        var candidate = Collapse(value ?? "");
        return type switch
        {
            LetterRequestType.Arithmetic when Occurs(source, candidate) && ArithmeticCalculator.Evaluate(candidate) is { } result =>
                new LetterRequest { Type = type, Text = Short(quote), Value = result.Expression, Answer = result.Value },
            LetterRequestType.CodeWord when Occurs(source, candidate) && IsCodeWord(candidate) =>
                new LetterRequest { Type = type, Text = Short(quote), Value = candidate },
            _ => new LetterRequest { Type = type, Text = Short(quote) },
        };
    }

    public bool CanClose => Answer is not null;

    // Почему просьбу нельзя закрыть автоматически; null — можно
    public string? Problem => CanClose
        ? null
        : Type switch
        {
            LetterRequestType.Arithmetic => "пример не посчитан",
            LetterRequestType.Salary => "в профиле нет желаемой зарплаты",
            LetterRequestType.TestTask => "в профиле не указана готовность к тестовому",
            LetterRequestType.CodeWord when Value is null => "кодовое слово не прошло проверку",
            LetterRequestType.CodeWord => "кодовое слово ждёт подтверждения",
            _ => "прочая просьба — решает человек",
        };

    // Кодовое слово — только по подтверждению человека (#72, В8)
    public LetterRequest ConfirmCodeWord()
    {
        if (Type != LetterRequestType.CodeWord || Value is null)
            throw new DomainException("Подтвердить можно только проверенное кодовое слово");

        return Copy(Value, Closed);
    }

    // Зарплата и готовность — только из профиля: при пустом профиле ответа нет, модель ничего не подставляет
    public LetterRequest WithProfile(CandidateProfile? profile) => Type switch
    {
        LetterRequestType.Salary => Copy(profile?.DesiredSalaryText, Closed),
        LetterRequestType.TestTask => Copy(profile?.TestTaskText, Closed),
        _ => this,
    };

    public LetterRequest WithClosed(bool closed) => Copy(Answer, closed);

    private LetterRequest Copy(string? answer, bool closed) => new() { Type = Type, Text = Text, Value = Value, Answer = answer, Closed = closed };

    private static string Short(string text) => text.Length > MaxText ? text[..MaxText] : text;

    // Буквы и цифры, одно слово. Телефон цифрами — не кодовое слово, ссылка и почта не пройдут по символам
    private static bool IsCodeWord(string value) =>
        value.Length is > 0 and <= MaxCodeWord && value.All(c => char.IsLetterOrDigit(c) || c is '-' or '_')
        && char.IsLetterOrDigit(value[0]) && value.Any(char.IsLetter);

    // Дословно и не внутри другого слова или числа
    private static bool Occurs(string text, string value)
    {
        if (value == "")
            return false;

        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + 1, StringComparison.Ordinal))
        {
            var end = i + value.Length;
            if ((i == 0 || !char.IsLetterOrDigit(text[i - 1])) && (end == text.Length || !char.IsLetterOrDigit(text[end])))
                return true;
        }

        return false;
    }

    private static string Collapse(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
