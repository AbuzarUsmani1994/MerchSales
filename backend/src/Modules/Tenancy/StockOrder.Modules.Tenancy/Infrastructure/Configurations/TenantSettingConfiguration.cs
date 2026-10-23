using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockOrder.Modules.Tenancy.Domain;

namespace StockOrder.Modules.Tenancy.Infrastructure.Configurations;

public sealed class TenantSettingConfiguration : IEntityTypeConfiguration<TenantSetting>
{
    public void Configure(EntityTypeBuilder<TenantSetting> builder)
    {
        builder.ToTable("TenantSetting");
        builder.HasKey(s => new { s.TenantId, s.SettingKey });

        builder.Property(s => s.SettingKey).HasColumnType("varchar(100)").IsRequired();
        builder.Property(s => s.Value).IsRequired();
        builder.Property(s => s.RowVer).IsRowVersion();
    }
}
