using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockOrder.Modules.Catalog.Domain;
using StockOrder.SharedKernel.Entities;

namespace StockOrder.Modules.Catalog.Infrastructure;

internal static class TenantEntityMapping
{
    public static void MapKeys<T>(this EntityTypeBuilder<T> builder, string table)
        where T : TenantEntity
    {
        builder.ToTable(table);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.ClusterKey).ValueGeneratedOnAdd();
        builder.Property(e => e.RowVer).IsRowVersion();
        builder.HasIndex(e => new { e.TenantId, e.Id }).IsUnique();
    }
}

public sealed class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> builder)
    {
        builder.MapKeys("UnitOfMeasure");
        builder.Property(u => u.Code).HasMaxLength(20).IsRequired();
        builder.Property(u => u.Name).HasMaxLength(100).IsRequired();
    }
}

public sealed class ProductTypeConfiguration : IEntityTypeConfiguration<ProductType>
{
    public void Configure(EntityTypeBuilder<ProductType> builder)
    {
        builder.MapKeys("ProductType");
        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Behaviour).HasColumnType("varchar(20)");
        builder.Property(t => t.DefaultTracking).HasColumnType("varchar(20)");
        builder.Property(t => t.DefaultIssueRule).HasColumnType("varchar(10)");
    }
}

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.MapKeys("Category");
        builder.Property(c => c.Name).HasMaxLength(150).IsRequired();
    }
}

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.MapKeys("Product");
        builder.Property(p => p.Sku).HasMaxLength(64).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.ShortName).HasMaxLength(60);
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.Property(p => p.TrackingMode).HasColumnType("varchar(20)");
        builder.Property(p => p.IssueRule).HasColumnType("varchar(10)");
        builder.Ignore(p => p.PricingUomId);
        builder.HasIndex(p => new { p.TenantId, p.Sku }).IsUnique();
    }
}

public sealed class ProductUomConversionConfiguration : IEntityTypeConfiguration<ProductUomConversion>
{
    public void Configure(EntityTypeBuilder<ProductUomConversion> builder)
    {
        builder.MapKeys("ProductUomConversion");
        builder.Property(c => c.FactorToBase).HasPrecision(18, 6);
    }
}

public sealed class PriceListConfiguration : IEntityTypeConfiguration<PriceList>
{
    public void Configure(EntityTypeBuilder<PriceList> builder)
    {
        builder.MapKeys("PriceList");
        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Currency).HasColumnType("char(3)");
    }
}

public sealed class PriceListItemConfiguration : IEntityTypeConfiguration<PriceListItem>
{
    public void Configure(EntityTypeBuilder<PriceListItem> builder)
    {
        builder.MapKeys("PriceListItem");
        builder.Property(i => i.MinQuantity).HasPrecision(18, 6);
        builder.Property(i => i.UnitPrice).HasPrecision(19, 4);
    }
}

public sealed class ProductBranchConfiguration : IEntityTypeConfiguration<ProductBranch>
{
    public void Configure(EntityTypeBuilder<ProductBranch> builder)
    {
        builder.MapKeys("ProductBranch");
        builder.HasIndex(pb => new { pb.TenantId, pb.ProductId, pb.BranchId }).IsUnique();
    }
}
