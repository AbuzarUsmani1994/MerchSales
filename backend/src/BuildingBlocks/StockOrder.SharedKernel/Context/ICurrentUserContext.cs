namespace StockOrder.SharedKernel.Context;

/// <summary>
/// Who is making this request, derived only from the validated access token (section 6) —
/// never from a header, route or body. Resolved per-request by the Api host and consumed by
/// every module's Application layer for authorization, audit and RLS session context.
/// Which branches the user may act on comes from <see cref="IAccessScope"/>, which needs the database.
/// </summary>
public interface ICurrentUserContext
{
    bool IsAuthenticated { get; }
    Guid TenantId { get; }
    string TenantCode { get; }
    Guid UserId { get; }
    Guid SecurityStamp { get; }
    Guid? DeviceId { get; }

    /// <summary>True only for the platform operator tenant's cross-tenant jobs/admin endpoints (section 6).</summary>
    bool IsPlatformAdmin { get; }
}
