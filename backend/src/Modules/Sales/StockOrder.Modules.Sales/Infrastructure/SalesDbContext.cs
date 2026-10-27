using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Sales.Domain;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Entities;

namespace StockOrder.Modules.Sales.Infrastructure;

/// <summary>Owns the sales schema (section 5.2).</summary>
public sealed class SalesDbContext(DbContextOptions<SalesDbContext> options, ICurrentUserContext currentUser)
    : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<LoyaltyMember> LoyaltyMembers => Set<LoyaltyMember>();
    public DbSet<LoyaltyVisit> LoyaltyVisits => Set<LoyaltyVisit>();
    public DbSet<LoyaltyRule> LoyaltyRules => Set<LoyaltyRule>();
    public DbSet<LoyaltyBranch> LoyaltyBranches => Set<LoyaltyBranch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("sales");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SalesDbContext).Assembly);
        modelBuilder.ApplyTenantQueryFilters(currentUser);
    }
}

internal static class SalesMapping
{
    public static void MapKeys<T>(this EntityTypeBuilder<T> builder, string table, bool hasRowVersion = true)
        where T : TenantEntity
    {
        builder.ToTable(table);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.ClusterKey).ValueGeneratedOnAdd();
        if (hasRowVersion)
        {
            builder.Property(e => e.RowVer).IsRowVersion();
        }
        else
        {
            builder.Ignore(e => e.RowVer);
        }
    }

    public static PropertyBuilder<decimal> Money(this PropertyBuilder<decimal> property) => property.HasPrecision(19, 4);

    public static PropertyBuilder<decimal> Qty(this PropertyBuilder<decimal> property) => property.HasPrecision(18, 6);

    public static PropertyBuilder<decimal> Percent(this PropertyBuilder<decimal> property) => property.HasPrecision(9, 4);
}

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.MapKeys("Customer");
        builder.Property(c => c.Code).HasMaxLength(32);
        builder.Property(c => c.Name).HasMaxLength(200);
        builder.Property(c => c.Phone).HasMaxLength(40);
    }
}

public sealed class LoyaltyMemberConfiguration : IEntityTypeConfiguration<LoyaltyMember>
{
    public void Configure(EntityTypeBuilder<LoyaltyMember> builder)
    {
        builder.MapKeys("LoyaltyMember");
        builder.Property(m => m.Phone).HasColumnType("varchar(15)");
        builder.Property(m => m.Status).HasColumnType("varchar(10)");
    }
}

/// <summary>Append-only: no RowVer column; rows are only ever added.</summary>
public sealed class LoyaltyVisitConfiguration : IEntityTypeConfiguration<LoyaltyVisit>
{
    public void Configure(EntityTypeBuilder<LoyaltyVisit> builder)
    {
        builder.MapKeys("LoyaltyVisit", hasRowVersion: false);
        builder.Property(v => v.Kind).HasColumnType("varchar(16)");
        builder.Property(v => v.BillAmount).Money();
        builder.Property(v => v.RewardAmount).HasPrecision(19, 4);
    }
}

public sealed class LoyaltyRuleConfiguration : IEntityTypeConfiguration<LoyaltyRule>
{
    public void Configure(EntityTypeBuilder<LoyaltyRule> builder)
    {
        builder.ToTable("LoyaltyRule");
        builder.HasKey(r => r.TenantId);
        builder.Property(r => r.MinVisitBill).Money();
        builder.Property(r => r.RewardAmount).Money();
        builder.Property(r => r.MinRewardBill).Money();
        builder.Property(r => r.RowVer).IsRowVersion();
    }
}

public sealed class LoyaltyBranchConfiguration : IEntityTypeConfiguration<LoyaltyBranch>
{
    public void Configure(EntityTypeBuilder<LoyaltyBranch> builder)
    {
        builder.ToTable("LoyaltyBranch");
        builder.HasKey(b => new { b.TenantId, b.BranchId });
        builder.Property(b => b.RowVer).IsRowVersion();
    }
}

public sealed class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> builder)
    {
        builder.MapKeys("SalesOrder");
        builder.Property(o => o.DocumentNo).HasMaxLength(40);
        builder.Property(o => o.DeviceDocumentNo).HasMaxLength(40);
        builder.Property(o => o.Channel).HasColumnType("varchar(20)");
        builder.Property(o => o.Status).HasColumnType("varchar(20)");
        builder.Property(o => o.HoldReason).HasMaxLength(300);
        builder.Property(o => o.Currency).HasColumnType("char(3)");
        builder.Property(o => o.SubTotal).Money();
        builder.Property(o => o.DiscountTotal).Money();
        builder.Property(o => o.TaxTotal).Money();
        builder.Property(o => o.ChargesTotal).Money();
        builder.Property(o => o.GrandTotal).Money();
        builder.Property(o => o.PaidAmount).Money();
        builder.Property(o => o.BackOrderPolicy).HasColumnType("varchar(20)");
        builder.Property(o => o.Notes).HasMaxLength(1000);
        builder.Property(o => o.LoyaltyPhone).HasColumnType("varchar(15)");
        builder.Property(o => o.CancelNotes).HasMaxLength(500);

        builder.HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.SalesOrderId);
        builder.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(o => o.StatusHistory).WithOne().HasForeignKey(h => h.SalesOrderId);
        builder.Navigation(o => o.StatusHistory).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class SalesOrderLineConfiguration : IEntityTypeConfiguration<SalesOrderLine>
{
    public void Configure(EntityTypeBuilder<SalesOrderLine> builder)
    {
        builder.MapKeys("SalesOrderLine", hasRowVersion: false);
        builder.Property(l => l.Quantity).Qty();
        builder.Property(l => l.QtyBase).Qty();
        builder.Property(l => l.FulfilledQtyBase).Qty();
        builder.Property(l => l.UnitPrice).Money();
        builder.Property(l => l.DiscountPercent).Percent();
        builder.Property(l => l.DiscountAmount).Money();
        builder.Property(l => l.TaxRatePercent).Percent();
        builder.Property(l => l.TaxAmount).Money();
        builder.Property(l => l.LineTotal).Money();
        builder.Property(l => l.Status).HasColumnType("varchar(20)");
        builder.Property(l => l.Notes).HasMaxLength(500);
    }
}

public sealed class SalesOrderStatusChangeConfiguration : IEntityTypeConfiguration<SalesOrderStatusChange>
{
    public void Configure(EntityTypeBuilder<SalesOrderStatusChange> builder)
    {
        builder.ToTable("SalesOrderStatusHistory");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedOnAdd();
        builder.Property(h => h.FromStatus).HasColumnType("varchar(20)");
        builder.Property(h => h.ToStatus).HasColumnType("varchar(20)");
        builder.Property(h => h.Reason).HasMaxLength(500);
    }
}

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.MapKeys("Invoice");
        builder.Property(i => i.DocumentNo).HasMaxLength(40);
        builder.Property(i => i.Status).HasColumnType("varchar(20)");
        builder.Property(i => i.Currency).HasColumnType("char(3)");
        builder.Property(i => i.SubTotal).Money();
        builder.Property(i => i.DiscountTotal).Money();
        builder.Property(i => i.TaxTotal).Money();
        builder.Property(i => i.ChargesTotal).Money();
        builder.Property(i => i.GrandTotal).Money();
        builder.Property(i => i.AmountPaid).Money();
        builder.HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId);
        builder.Navigation(i => i.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.MapKeys("InvoiceLine", hasRowVersion: false);
        builder.Property(l => l.Description).HasMaxLength(300);
        builder.Property(l => l.Quantity).Qty();
        builder.Property(l => l.QtyBase).Qty();
        builder.Property(l => l.UnitPrice).Money();
        builder.Property(l => l.DiscountAmount).Money();
        builder.Property(l => l.TaxRatePercent).Percent();
        builder.Property(l => l.TaxAmount).Money();
        builder.Property(l => l.LineTotal).Money();
    }
}
