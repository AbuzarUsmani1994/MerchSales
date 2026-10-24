using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockOrder.Modules.Inventory.Domain;

namespace StockOrder.Modules.Inventory.Infrastructure.Configurations;

public sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("Warehouse");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.ClusterKey).ValueGeneratedOnAdd();
        builder.Property(w => w.Code).HasMaxLength(32).IsRequired();
        builder.Property(w => w.Name).HasMaxLength(150).IsRequired();
        builder.Property(w => w.RowVer).IsRowVersion();

        builder.HasIndex(w => new { w.TenantId, w.Id }).IsUnique();
        builder.HasIndex(w => new { w.TenantId, w.Code }).IsUnique();
        builder.HasIndex(w => w.ClusterKey).IsUnique();
    }
}

public sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("Location");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.ClusterKey).ValueGeneratedOnAdd();
        builder.Property(l => l.Code).HasMaxLength(32).IsRequired();
        builder.Property(l => l.Name).HasMaxLength(150).IsRequired();
        builder.Property(l => l.LocationType).HasColumnType("varchar(20)").IsRequired();
        builder.Property(l => l.RowVer).IsRowVersion();

        builder.HasIndex(l => new { l.TenantId, l.Id }).IsUnique();
        builder.HasIndex(l => new { l.TenantId, l.WarehouseId, l.Code }).IsUnique();
        builder.HasIndex(l => l.ClusterKey).IsUnique();
    }
}

public sealed class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> builder)
    {
        builder.ToTable("Batch");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.ClusterKey).ValueGeneratedOnAdd();
        builder.Property(b => b.BatchNo).HasMaxLength(64).IsRequired();
        builder.Property(b => b.Status).HasColumnType("varchar(20)").IsRequired();
        builder.Property(b => b.RowVer).IsRowVersion();

        builder.HasIndex(b => new { b.TenantId, b.ProductId, b.BatchNo }).IsUnique();
        builder.HasIndex(b => b.ClusterKey).IsUnique();
    }
}
