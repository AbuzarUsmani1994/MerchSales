using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using StockOrder.SharedKernel.Context;

namespace StockOrder.Infrastructure.Context;

/// <summary>
/// Reads the tenant and user only from the validated access token's claims (section 6), set
/// by JWT bearer authentication earlier in the pipeline — never from a header, route or body.
/// </summary>
public sealed class HttpCurrentUserContext(IHttpContextAccessor httpContextAccessor) : ICurrentUserContext
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public Guid TenantId => ParseClaim("tid");

    public string TenantCode => User?.FindFirstValue("tcode") ?? string.Empty;

    public Guid UserId => ParseClaim(ClaimTypes.NameIdentifier, "sub");

    public Guid SecurityStamp => ParseClaim("sst");

    public Guid? DeviceId => Guid.TryParse(User?.FindFirstValue("did"), out var id) ? id : null;

    // Branch scoping (section 6) is read directly from identity.UserRole by the handlers
    // that need it in Phase 1 (Sales, Inventory); no Phase 0 feature filters by branch yet.
    public IReadOnlyCollection<Guid> AllowedBranchIds => [];

    public bool IsPlatformAdmin => TenantCode == "PLATFORM";

    private Guid ParseClaim(params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = User?.FindFirstValue(claimType);
            if (Guid.TryParse(value, out var result))
            {
                return result;
            }
        }

        return Guid.Empty;
    }
}
