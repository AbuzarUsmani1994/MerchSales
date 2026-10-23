using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockOrder.Modules.Identity.Domain;

namespace StockOrder.Modules.Identity.Infrastructure.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Role");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.ClusterKey).ValueGeneratedOnAdd();
        builder.Property(r => r.Name).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(500);
        builder.Property(r => r.RowVer).IsRowVersion();

        builder.HasIndex(r => new { r.TenantId, r.Id }).IsUnique();
        builder.HasIndex(r => new { r.TenantId, r.Name }).IsUnique();
        builder.HasIndex(r => r.ClusterKey).IsUnique();
    }
}

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permission");
        builder.HasKey(p => p.Code);

        builder.Property(p => p.Code).HasColumnType("varchar(100)");
        builder.Property(p => p.Module).HasColumnType("varchar(40)").IsRequired();
        builder.Property(p => p.Description).HasMaxLength(300).IsRequired();
    }
}

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermission");
        builder.HasKey(rp => new { rp.TenantId, rp.RoleId, rp.PermissionCode });
        builder.Property(rp => rp.PermissionCode).HasColumnType("varchar(100)");
    }
}

public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRole");
        builder.HasKey(ur => ur.Id);

        builder.Property(ur => ur.ClusterKey).ValueGeneratedOnAdd();

        builder.HasIndex(ur => new { ur.TenantId, ur.UserId, ur.RoleId, ur.BranchId }).IsUnique();
        builder.HasIndex(ur => ur.ClusterKey).IsUnique();
    }
}
