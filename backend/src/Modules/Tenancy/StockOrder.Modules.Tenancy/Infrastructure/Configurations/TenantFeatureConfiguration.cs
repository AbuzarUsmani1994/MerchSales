using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockOrder.Modules.Tenancy.Domain;

namespace StockOrder.Modules.Tenancy.Infrastructure.Configurations;

public sealed class TenantFeatureConfiguration : IEntityTypeConfiguration<TenantFeature>
{
    public void Configure(EntityTypeBuilder<TenantFeature> builder)
    {
        builder.ToTable("TenantFeature");
        builder.HasKey(f => new { f.TenantId, f.FeatureKey });

        builder.Property(f => f.FeatureKey).HasColumnType("varchar(100)").IsRequired();
        builder.Property(f => f.RowVer).IsRowVersion();
    }
}
