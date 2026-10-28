using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Restaurant.Contracts;
using StockOrder.Modules.Restaurant.Domain;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Ids;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Restaurant.Infrastructure;

internal sealed class Kitchen(
    RestaurantDbContext db, ICurrentUserContext currentUser, IKitchenBoardNotifier notifier, IClock clock, IIdGenerator idGenerator) : IKitchen
{
    public async Task<KitchenTicketInfo> SendRoundAsync(KitchenRound round, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var earlier = await db.KitchenTickets
            .Where(t => t.TenantId == tenantId && t.SalesOrderId == round.OrderId)
            .OrderByDescending(t => t.RoundNo)
            .Select(t => new { t.RoundNo, t.BusinessDate })
            .FirstOrDefaultAsync(ct);

        // A later round stays on the trading day of the order's first ticket.
        var ticket = KitchenTicket.Create(
            tenantId, idGenerator.NewId(), round.OrderId, round.BranchId, earlier?.BusinessDate ?? round.BusinessDate, round.OrderNo,
            (earlier?.RoundNo ?? 0) + 1, round.Channel, round.TableName, round.Notes, clock.UtcNow, currentUser.UserId);
        foreach (var line in round.Lines)
        {
            ticket.AddLine(idGenerator.NewId(), line.OrderLineId, line.ProductId, line.ProductName, line.Quantity, line.Notes);
        }

        db.KitchenTickets.Add(ticket);
        await db.SaveChangesAsync(ct);
        notifier.BranchChanged(round.BranchId);
        return new KitchenTicketInfo(ticket.Id, ticket.RoundNo);
    }

    public async Task CancelOrderAsync(Guid orderId, Guid branchId, CancellationToken ct)
    {
        var tickets = await db.KitchenTickets
            .Where(t => t.TenantId == currentUser.TenantId && t.SalesOrderId == orderId && KitchenTicketStatus.InKitchen.Contains(t.Status))
            .ToListAsync(ct);
        if (tickets.Count == 0)
        {
            return;
        }

        var now = clock.UtcNow;
        foreach (var ticket in tickets)
        {
            ticket.Cancel(now, currentUser.UserId);
        }

        await db.SaveChangesAsync(ct);
        notifier.BranchChanged(branchId);
    }

    public async Task CloseTableOrderAsync(Guid orderId, Guid branchId, CancellationToken ct)
    {
        var tickets = await db.KitchenTickets
            .Where(t => t.TenantId == currentUser.TenantId && t.SalesOrderId == orderId && KitchenTicketStatus.InKitchen.Contains(t.Status))
            .ToListAsync(ct);
        if (tickets.Count == 0)
        {
            return;
        }

        var now = clock.UtcNow;
        foreach (var ticket in tickets)
        {
            ticket.Apply(KitchenAction.Serve, currentUser.UserId, now);
        }

        await db.SaveChangesAsync(ct);
        notifier.BranchChanged(branchId);
    }

    public async Task<IReadOnlyDictionary<Guid, KitchenProgress>> GetProgressAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken ct)
    {
        if (orderIds.Count == 0)
        {
            return new Dictionary<Guid, KitchenProgress>();
        }

        var ids = orderIds.ToList();
        var counts = await db.KitchenTickets.AsNoTracking()
            .Where(t => t.TenantId == currentUser.TenantId && ids.Contains(t.SalesOrderId))
            .GroupBy(t => new { t.SalesOrderId, t.Status })
            .Select(g => new { g.Key.SalesOrderId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);

        return counts.GroupBy(c => c.SalesOrderId).ToDictionary(
            g => g.Key,
            g => new KitchenProgress(
                g.Where(c => c.Status == KitchenTicketStatus.Queued).Sum(c => c.Count),
                g.Where(c => c.Status == KitchenTicketStatus.Preparing).Sum(c => c.Count),
                g.Where(c => c.Status == KitchenTicketStatus.Ready).Sum(c => c.Count),
                g.Where(c => c.Status == KitchenTicketStatus.Served).Sum(c => c.Count)));
    }
}

internal sealed class DiningTables(RestaurantDbContext db, ICurrentUserContext currentUser) : IDiningTables
{
    public async Task<DiningTableInfo?> GetAsync(Guid tableId, CancellationToken ct) =>
        await db.DiningTables.AsNoTracking()
            .Where(t => t.TenantId == currentUser.TenantId && t.Id == tableId)
            .Select(t => new DiningTableInfo(t.Id, t.BranchId, t.Name, t.IsActive))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> tableIds, CancellationToken ct)
    {
        var ids = tableIds.ToList();
        return await db.DiningTables.AsNoTracking()
            .Where(t => t.TenantId == currentUser.TenantId && ids.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);
    }
}
