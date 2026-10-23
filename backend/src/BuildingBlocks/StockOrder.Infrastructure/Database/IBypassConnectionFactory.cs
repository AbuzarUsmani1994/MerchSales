using Microsoft.Data.SqlClient;

namespace StockOrder.Infrastructure.Database;

/// <summary>
/// Opens a connection with the RLS bypass flag set (section 6). Do not inject this directly
/// outside the handful of approved call sites — <see cref="ITenantLookupService"/>,
/// <see cref="ICrossTenantRunner"/>, <see cref="ITenantProvisioningConnection"/>, and the
/// Worker's outbox dispatcher and mobile version-gate middleware (both of which read across
/// every tenant by nature). It can't be marked <c>internal</c>: ASP.NET Core's
/// convention-based middleware needs it as a public method parameter on
/// <c>MobileVersionGateMiddleware</c> (CS0051 forbids a less-accessible parameter type on a
/// public member). <c>StockOrder.ArchitectureTests</c> asserts the full allow-list instead,
/// so a new, unapproved consumer fails the build the same way an <c>internal</c> modifier
/// would have blocked it at compile time.
/// </summary>
public interface IBypassConnectionFactory
{
    Task<SqlConnection> OpenAsync(CancellationToken ct);
}
