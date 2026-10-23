using Microsoft.AspNetCore.DataProtection;

namespace StockOrder.Modules.Identity.Infrastructure.Auth;

/// <summary>TOTP secrets are stored encrypted with ASP.NET Data Protection (section 10), never in plain text.</summary>
public interface IMfaSecretProtector
{
    string Protect(string plainBase32Secret);
    string Unprotect(string protectedSecret);
}

internal sealed class MfaSecretProtector(IDataProtectionProvider provider) : IMfaSecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("StockOrder.Identity.MfaSecret.v1");

    public string Protect(string plainBase32Secret) => _protector.Protect(plainBase32Secret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}
