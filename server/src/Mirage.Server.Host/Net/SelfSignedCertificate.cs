using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Mirage.Server.Host.Net;

/// <summary>
/// The throwaway certificate both listeners present. Every client trusts it unconditionally, so it buys
/// encryption on the wire and not identity — which is the whole of what is wanted here, and why nothing
/// has to be installed or renewed for a server to run.
/// </summary>
public static class SelfSignedCertificate
{
    /// <summary>The subject name, which is also the host name every client authenticates against.</summary>
    public const string SubjectName = "mirage-server";

    public static X509Certificate2 Create()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={SubjectName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var temp = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        // Exported and re-imported as ephemeral so Schannel can reach the private key through SslStream.
        return X509CertificateLoader.LoadPkcs12(temp.Export(X509ContentType.Pfx), password: null);
    }
}
