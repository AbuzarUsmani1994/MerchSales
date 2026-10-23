using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockOrder.Modules.Identity.Domain;

namespace StockOrder.Modules.Identity.Infrastructure.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("User");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.ClusterKey).ValueGeneratedOnAdd();
        builder.Property(u => u.Username).HasMaxLength(100).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(256);
        builder.Property(u => u.Phone).HasMaxLength(40);
        builder.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
        builder.Property(u => u.MfaSecretProtected).HasMaxLength(500);
        builder.Property(u => u.PinHash).HasMaxLength(500);
        builder.Property(u => u.RowVer).IsRowVersion();

        builder.HasIndex(u => new { u.TenantId, u.Id }).IsUnique();
        builder.HasIndex(u => new { u.TenantId, u.Username }).IsUnique();
        builder.HasIndex(u => u.ClusterKey).IsUnique();
    }
}
