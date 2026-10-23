using OtpNet;

namespace StockOrder.Modules.Identity.Infrastructure.Auth;

/// <summary>TOTP MFA required at web login for admin-level users (section 10).</summary>
public static class TotpVerifier
{
    public static bool Verify(string base32Secret, string code)
    {
        var totp = new Totp(Base32Encoding.ToBytes(base32Secret));
        return totp.VerifyTotp(code, out _, VerificationWindow.RfcSpecifiedNetworkDelay);
    }

    public static string GenerateSecret() => Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
}
