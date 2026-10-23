using System.Security.Cryptography;

namespace StockOrder.SharedKernel.Ids;

/// <summary>
/// Generates the one-time password shown when a new user or tenant owner is created
/// (sections 10, 11); never persisted in plain text. Lives in SharedKernel because both
/// Identity (user creation) and Platform (tenant provisioning) need it without referencing
/// each other's implementation projects (section 5.1).
/// </summary>
public static class OneTimePasswordGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    public static string Generate(int length = 12)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }
}
