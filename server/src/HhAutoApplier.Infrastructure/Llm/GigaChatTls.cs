using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace HhAutoApplier.Infrastructure.Llm;

// Доверие к корню НУЦ Минцифры (Russian Trusted Root CA) только у HttpClient-ов ГигаЧата. Не системно в образе: иначе этому корню
// доверяли бы все исходящие соединения (hh.ru, Telegram), а нужен он только двум адресам Сбера (#67).
// Файл — Certificates/russian_trusted_root_ca.pem, ссылка со страницы gosuslugi.ru/landing/crt_linux; отпечатки там не публикуются,
// поэтому 2026-11-08 SHA256 сверен с корнем, который отдают в TLS ngw.devices.sberbank.ru и gigachat.devices.sberbank.ru
public static class GigaChatTls
{
    public const string RootSha256 = "D26D2D0231B7C39F92CC738512BA54103519E4405D68B5BD703E9788CA8ECF31";

    private const string ResourceSuffix = "russian_trusted_root_ca.pem";

    private const string ServerAuthOid = "1.3.6.1.5.5.7.3.1";

    public static X509Certificate2 LoadRoot()
    {
        var assembly = typeof(GigaChatTls).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(ResourceSuffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        var root = X509Certificate2.CreateFromPem(reader.ReadToEnd());

        var actual = Convert.ToHexString(SHA256.HashData(root.RawData));
        if (actual != RootSha256)
            throw new InvalidOperationException($"Сертификат НУЦ Минцифры не совпадает с закреплённым: SHA256 {actual}");

        return root;
    }

    public static HttpMessageHandler CreateHandler(X509Certificate2 root) => new SocketsHttpHandler
    {
        SslOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (_, certificate, chain, errors) => Validate(root, certificate, chain, errors),
        },
    };

    // Системное доверие принимается как есть, ошибка имени узла — никогда. Цепочка пересобирается с единственным доверенным корнем
    public static bool Validate(X509Certificate2 root, X509Certificate? certificate, X509Chain? presented, SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None)
            return true;

        if (errors != SslPolicyErrors.RemoteCertificateChainErrors || certificate is null)
            return false;

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(root);
        // Как и у обычных соединений .NET по умолчанию: отзыв не проверяется
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid(ServerAuthOid));
        if (presented is not null)
        {
            chain.ChainPolicy.ExtraStore.AddRange(presented.ChainPolicy.ExtraStore);
            foreach (var element in presented.ChainElements)
                chain.ChainPolicy.ExtraStore.Add(element.Certificate);
        }

        using var leaf = new X509Certificate2(certificate);
        return chain.Build(leaf);
    }
}