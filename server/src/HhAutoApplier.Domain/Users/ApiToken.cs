using System.Security.Cryptography;
using System.Text;

namespace HhAutoApplier.Domain.Users;

// Агрегат: персональный токен пользователя для расширения (заголовок X-Api-Key). Хранится только хэш:
// сам токен показывается один раз при создании. Отозванный токен не действует
public class ApiToken
{
    // Для EF
    private ApiToken()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Name { get; private set; } = "";

    // SHA-256 токена; у токена 256 бит случайности, медленный хэш не нужен
    public string Hash { get; private set; } = "";

    // Начало токена — чтобы узнать его в списке
    public string Prefix { get; private set; } = "";

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool Active => RevokedAt is null;

    // Токен и его открытое значение (только сейчас)
    public static (ApiToken Token, string Value) Create(Guid userId, string name, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("У токена нет названия");

        var value = Marker + Base64Url(RandomNumberGenerator.GetBytes(32));
        var token = new ApiToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name.Trim(),
            Hash = HashOf(value),
            Prefix = value[..(Marker.Length + 6)],
            CreatedAt = at,
        };
        return (token, value);
    }

    public void Revoke(DateTimeOffset at)
    {
        if (RevokedAt is not null)
            throw new DomainException("Токен уже отозван");

        RevokedAt = at;
    }

    // Значение из заголовка — персональный токен, а не общий ключ
    public static bool IsToken(string value) => value.StartsWith(Marker, StringComparison.Ordinal);

    public static string HashOf(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private const string Marker = "hh_";
}