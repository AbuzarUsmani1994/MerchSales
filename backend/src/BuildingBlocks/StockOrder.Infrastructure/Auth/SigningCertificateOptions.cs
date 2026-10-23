namespace StockOrder.Infrastructure.Auth;

public sealed class SigningCertificateOptions
{
    public const string SectionName = "Auth";

    public string? SigningCertificatePath { get; init; }
    public string? SigningCertificatePassword { get; init; }
    public string? SigningCertificateThumbprint { get; init; }
}
