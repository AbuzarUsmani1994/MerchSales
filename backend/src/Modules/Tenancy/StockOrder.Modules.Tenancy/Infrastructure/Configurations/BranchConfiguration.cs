using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockOrder.Modules.Tenancy.Domain;

namespace StockOrder.Modules.Tenancy.Infrastructure.Configurations;

public sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("Branch");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.ClusterKey).ValueGeneratedOnAdd();
        builder.Property(b => b.Code).HasMaxLength(32).IsRequired();
        builder.Property(b => b.Name).HasMaxLength(200).IsRequired();
        builder.Property(b => b.Address).HasMaxLength(500);
        builder.Property(b => b.Phone).HasMaxLength(40);
        builder.Property(b => b.RowVer).IsRowVersion();

        builder.HasIndex(b => new { b.TenantId, b.Id }).IsUnique();
        builder.HasIndex(b => new { b.TenantId, b.Code }).IsUnique();
        builder.HasIndex(b => b.ClusterKey).IsUnique();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(b => b.TenantId).OnDelete(DeleteBehavior.NoAction);
    }
}
