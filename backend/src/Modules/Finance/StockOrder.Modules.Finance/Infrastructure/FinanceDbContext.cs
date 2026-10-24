using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Finance.Domain;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Entities;

namespace StockOrder.Modules.Finance.Infrastructure;

/// <summary>Owns the finance schema (section 5.2). finance.PartyLedgerEntry is append-only and written with plain inserts.</summary>
public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options, ICurrentUserContext currentUser)
    : DbContext(options)
{
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<CashSession> CashSessions => Set<CashSession>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<AccountingPeriod> AccountingPeriods => Set<AccountingPeriod>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("finance");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinanceDbContext).Assembly);
        modelBuilder.ApplyTenantQueryFilters(currentUser);
    }
}

internal static class FinanceMapping
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
}

public sealed class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.MapKeys("PaymentMethod");
        builder.Property(m => m.Code).HasMaxLength(20);
        builder.Property(m => m.Name).HasMaxLength(100);
        builder.Property(m => m.Kind).HasColumnType("varchar(20)");
    }
}

public sealed class CashSessionConfiguration : IEntityTypeConfiguration<CashSession>
{
    public void Configure(EntityTypeBuilder<CashSession> builder)
    {
        builder.MapKeys("CashSession");
        builder.Property(s => s.Status).HasColumnType("varchar(10)");
        builder.Property(s => s.OpeningFloat).HasPrecision(19, 4);
        builder.Property(s => s.ExpectedCash).HasPrecision(19, 4);
        builder.Property(s => s.CountedCash).HasPrecision(19, 4);
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.MapKeys("Payment");
        builder.Property(p => p.DocumentNo).HasMaxLength(40);
        builder.Property(p => p.Direction).HasColumnType("varchar(3)");
        builder.Property(p => p.PartyType).HasColumnType("varchar(10)");
        builder.Property(p => p.Amount).HasPrecision(19, 4);
        builder.Property(p => p.Currency).HasColumnType("char(3)");
        builder.Property(p => p.Reference).HasMaxLength(100);
        builder.Property(p => p.Status).HasColumnType("varchar(20)");
        builder.Property(p => p.Notes).HasMaxLength(500);
    }
}

public sealed class PaymentAllocationConfiguration : IEntityTypeConfiguration<PaymentAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentAllocation> builder)
    {
        builder.MapKeys("PaymentAllocation", hasRowVersion: false);
        builder.Property(a => a.DocumentType).HasColumnType("varchar(40)");
        builder.Property(a => a.Amount).HasPrecision(19, 4);
    }
}

public sealed class AccountingPeriodConfiguration : IEntityTypeConfiguration<AccountingPeriod>
{
    public void Configure(EntityTypeBuilder<AccountingPeriod> builder)
    {
        builder.MapKeys("AccountingPeriod");
        builder.Property(p => p.Status).HasColumnType("varchar(10)");
    }
}
