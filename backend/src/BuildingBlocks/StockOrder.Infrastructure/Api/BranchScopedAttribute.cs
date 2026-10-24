namespace StockOrder.Infrastructure.Api;

/// <summary>
/// By default an endpoint's permission policy is satisfied only by a role assigned for
/// <b>all branches</b>: most Phase 0 endpoints (features, settings, roles, number series,
/// creating branches) change tenant-wide data, which a branch manager must not touch.
/// Mark an endpoint (or controller) with this attribute when its handler itself checks
/// which branches the caller may act on through <see cref="StockOrder.SharedKernel.Context.IAccessScope"/>;
/// a role assigned for one branch then also satisfies the policy.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class BranchScopedAttribute : Attribute;
