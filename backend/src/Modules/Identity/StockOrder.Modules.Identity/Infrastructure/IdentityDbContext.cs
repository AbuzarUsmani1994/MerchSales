using Microsoft.EntityFrameworkCore;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Identity.Domain;
using StockOrder.SharedKernel.Context;

namespace StockOrder.Modules.Identity.Infrastructure;

/// <summary>Owns the identity schema only (section 5.2).</summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options, ICurrentUserContext currentUser)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);

        modelBuilder.ApplyTenantQueryFilters(currentUser);

        // identity.RolePermission and identity.UserRole have TenantId but no Id property
        // for ApplyTenantQueryFilters' TenantEntity check — filter them explicitly.
        modelBuilder.Entity<RolePermission>().HasQueryFilter(rp => rp.TenantId == currentUser.TenantId);
        modelBuilder.Entity<UserRole>().HasQueryFilter(ur => ur.TenantId == currentUser.TenantId);
        modelBuilder.Entity<RefreshToken>().HasQueryFilter(rt => rt.TenantId == currentUser.TenantId);
    }
}
