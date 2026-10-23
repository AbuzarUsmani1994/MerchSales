using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockOrder.Modules.Identity.Domain;

namespace StockOrder.Modules.Identity.Infrastructure.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshToken");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.ClusterKey).ValueGeneratedOnAdd();
        builder.Property(t => t.TokenHash).HasColumnType("varchar(64)").IsRequired();
        builder.Property(t => t.RevokedReason).HasColumnType("varchar(50)");
        builder.Property(t => t.CreatedByIp).HasColumnType("varchar(45)");

        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => new { t.TenantId, t.FamilyId });
        builder.HasIndex(t => t.ClusterKey).IsUnique();
    }
}
