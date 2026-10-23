using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace StockOrder.Infrastructure.Database;

/// <summary>
/// EF Core caches the compiled model per DbContext type by default. Every module DbContext's
/// <c>OnModelCreating</c> builds its tenant query filters by closing over the scoped
/// <see cref="StockOrder.SharedKernel.Context.ICurrentUserContext"/> passed into its
/// constructor (section 6) — with the default cache, the <em>second</em> tenant to use a
/// given DbContext type in the process's lifetime silently inherits the <em>first</em>
/// tenant's filter instead of its own, because the cached model's filter closures still
/// reference the first request's <c>ICurrentUserContext</c> instance. Keying the cache by
/// the DbContext instance itself forces a fresh model (and fresh closures) per request; the
/// model is small enough in Phase 0 that rebuilding it per request is an acceptable cost for
/// correctness.
/// </summary>
public sealed class PerScopeModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => (context, designTime);
}
