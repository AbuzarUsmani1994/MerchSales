namespace StockOrder.Modules.Sync.Contracts;

/// <summary>
/// One pullable entity (section 12: "Each module implements ISyncSource for its entities; the
/// Sync module only orchestrates"). Runs on the request's connection, so RLS already limits
/// it to the caller's tenant.
/// </summary>
public interface ISyncSource
{
    /// <summary>The entity's key in the pull tokens and changes maps, e.g. "settings".</summary>
    string Entity { get; }

    Task<SyncEntityPage> PullAsync(SyncPullRequest request, CancellationToken ct);
}

/// <param name="Token">What the device last received for this entity; null on first sync.</param>
/// <param name="UpperRowVersion">MIN_ACTIVE_ROWVERSION() captured once for the whole pull.</param>
public sealed record SyncPullRequest(string? Token, ulong UpperRowVersion, int Limit, Guid UserId);

public sealed record SyncEntityPage(IReadOnlyList<object> Rows, string Token, bool HasMore);

/// <summary>
/// The section 12 token rules for rowversion-tracked tables: rows with RowVer &gt; token and
/// &lt; upper, ordered by RowVer; the new token is the last row's RowVer when the page is full,
/// otherwise upper - 1 (everything below upper has been seen).
/// </summary>
public static class RowVersionToken
{
    /// <summary>An absent or unreadable token means "from the beginning": a full resend, never a gap.</summary>
    public static ulong Parse(string? token) =>
        ulong.TryParse(token, System.Globalization.NumberStyles.AllowHexSpecifier, null, out var value) ? value : 0;

    public static string Format(ulong rowVersion) => rowVersion.ToString("X16");

    public static string Next(IReadOnlyList<ulong> pageRowVersions, int limit, ulong upperRowVersion) =>
        pageRowVersions.Count >= limit ? Format(pageRowVersions[^1]) : Format(upperRowVersion - 1);

    /// <summary>rowversion as the big-endian binary(8) SQL Server compares it as.</summary>
    public static byte[] ToBinary(ulong rowVersion)
    {
        var bytes = BitConverter.GetBytes(rowVersion);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return bytes;
    }

    public static ulong FromBinary(byte[] rowVersion)
    {
        var bytes = (byte[])rowVersion.Clone();
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return BitConverter.ToUInt64(bytes);
    }
}
