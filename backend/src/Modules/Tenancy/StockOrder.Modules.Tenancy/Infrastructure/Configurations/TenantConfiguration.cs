using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockOrder.Modules.Tenancy.Domain;

namespace StockOrder.Modules.Tenancy.Infrastructure.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenant");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Code).HasMaxLength(32).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Industry).HasMaxLength(20).IsRequired();
        builder.Property(t => t.BaseCurrency).HasColumnType("char(3)").IsRequired();
        builder.Property(t => t.TimeZoneId).HasMaxLength(64).IsRequired();
        builder.Property(t => t.Status).HasMaxLength(20).IsRequired();
        builder.Property(t => t.RowVer).IsRowVersion();

        builder.HasIndex(t => t.Code).IsUnique();
    }
}
