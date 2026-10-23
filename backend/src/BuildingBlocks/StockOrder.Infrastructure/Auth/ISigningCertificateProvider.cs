using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace StockOrder.Infrastructure.Auth;

/// <summary>
/// RSA key from a PFX file or the Windows certificate store by thumbprint (section 10, 17).
/// In Development, if neither is configured, generates an ephemeral in-memory certificate
/// so the API can run locally without a real one — logged loudly, and refused outside
/// Development so nobody ships it by accident.
/// </summary>
public interface ISigningCertificateProvider
{
    X509Certificate2 Certificate { get; }
}

public sealed class SigningCertificateProvider : ISigningCertificateProvider
{
    public X509Certificate2 Certificate { get; }

    public SigningCertificateProvider(
        IOptions<SigningCertificateOptions> options, IHostEnvironment environment, ILogger<SigningCertificateProvider> logger)
    {
        var value = options.Value;

        if (!string.IsNullOrWhiteSpace(value.SigningCertificatePath))
        {
            Certificate = X509CertificateLoader.LoadPkcs12FromFile(
                value.SigningCertificatePath, value.SigningCertificatePassword);
            return;
        }

        if (!string.IsNullOrWhiteSpace(value.SigningCertificateThumbprint))
        {
            using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            var found = store.Certificates.Find(X509FindType.FindByThumbprint, value.SigningCertificateThumbprint, validOnly: false);
            if (found.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No certificate with thumbprint '{value.SigningCertificateThumbprint}' found in LocalMachine\\My.");
            }

            Certificate = found[0];
            return;
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Missing required configuration: Auth:SigningCertificatePath (or Auth:SigningCertificateThumbprint).");
        }

        logger.LogWarning(
            "No signing certificate configured — generating an ephemeral one for Development. " +
            "Tokens will not be valid across restarts. Set Auth:SigningCertificatePath for every other environment.");
        Certificate = GenerateEphemeralCertificate();
    }

    private static X509Certificate2 GenerateEphemeralCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=StockOrder.Dev", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }
}
