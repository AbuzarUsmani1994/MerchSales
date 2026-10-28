using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Restaurant.Domain;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Entities;

namespace StockOrder.Modules.Restaurant.Infrastructure;

/// <summary>Owns the restaurant schema (section 5.2, 016_RestaurantPack).</summary>
public sealed class RestaurantDbContext(DbContextOptions<RestaurantDbContext> options, ICurrentUserContext currentUser)
    : DbContext(options)
{
    public DbSet<DiningArea> DiningAreas => Set<DiningArea>();
    public DbSet<DiningTable> DiningTables => Set<DiningTable>();
    public DbSet<KitchenTicket> KitchenTickets => Set<KitchenTicket>();
    public DbSet<KitchenTicketLine> KitchenTicketLines => Set<KitchenTicketLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("restaurant");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RestaurantDbContext).Assembly);
        modelBuilder.ApplyTenantQueryFilters(currentUser);
    }
}

internal static class RestaurantMapping
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

public sealed class DiningAreaConfiguration : IEntityTypeConfiguration<DiningArea>
{
    public void Configure(EntityTypeBuilder<DiningArea> builder)
    {
        builder.MapKeys("DiningArea");
        builder.Property(a => a.Name).HasMaxLength(60);
    }
}

public sealed class DiningTableConfiguration : IEntityTypeConfiguration<DiningTable>
{
    public void Configure(EntityTypeBuilder<DiningTable> builder)
    {
        builder.MapKeys("DiningTable");
        builder.Property(t => t.Name).HasMaxLength(20);
    }
}

public sealed class KitchenTicketConfiguration : IEntityTypeConfiguration<KitchenTicket>
{
    public void Configure(EntityTypeBuilder<KitchenTicket> builder)
    {
        builder.MapKeys("KitchenTicket");
        builder.Property(t => t.Channel).HasColumnType("varchar(20)");
        builder.Property(t => t.Status).HasColumnType("varchar(10)");
        builder.Property(t => t.TableName).HasMaxLength(20);
        builder.Property(t => t.Notes).HasMaxLength(1000);
        builder.HasMany(t => t.Lines).WithOne().HasForeignKey(l => l.TicketId);
        builder.Navigation(t => t.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary>Never updated (UPDATE is denied): no RowVer column.</summary>
public sealed class KitchenTicketLineConfiguration : IEntityTypeConfiguration<KitchenTicketLine>
{
    public void Configure(EntityTypeBuilder<KitchenTicketLine> builder)
    {
        builder.MapKeys("KitchenTicketLine", hasRowVersion: false);
        builder.Property(l => l.ProductName).HasMaxLength(200);
        builder.Property(l => l.Quantity).HasPrecision(18, 6);
        builder.Property(l => l.Notes).HasMaxLength(500);
    }
}
