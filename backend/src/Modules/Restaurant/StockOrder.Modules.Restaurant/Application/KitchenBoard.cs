using Dapper;
using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Restaurant.Domain;
using StockOrder.Modules.Restaurant.Infrastructure;
using StockOrder.Modules.Tenancy.Contracts;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Restaurant.Application;

/// <summary>How long finished tickets stay in sight (docs/decisions.md "Tables and kitchen screen").</summary>
public static class KitchenTimings
{
    /// <summary>A ready ticket nobody marked served leaves the kitchen screen after this.</summary>
    public static readonly TimeSpan BoardReady = TimeSpan.FromMinutes(30);

    /// <summary>A cancelled ticket shows crossed out this long, so the cook sees it was stopped.</summary>
    public static readonly TimeSpan BoardCancelled = TimeSpan.FromMinutes(5);

    /// <summary>Anything older leaves the kitchen screen: a ticket nobody finished yesterday isn't today's work.</summary>
    public static readonly TimeSpan BoardMaxAge = TimeSpan.FromHours(12);

    /// <summary>A ready number leaves the customer screen after this even if nobody taps Served.</summary>
    public static readonly TimeSpan CustomerScreenReady = TimeSpan.FromMinutes(10);
}

public sealed record KitchenLineView(string Name, decimal Quantity, string? Notes);

/// <param name="Etag">Send back as If-Match with the next button.</param>
public sealed record KitchenTicketView(
    Guid Id, Guid OrderId, int OrderNo, int RoundNo, string Channel, string? TableName, string? Notes, string Status,
    DateTime CreatedAtUtc, DateTime? StartedAtUtc, DateTime? ReadyAtUtc, DateTime? CancelledAtUtc, string Etag,
    IReadOnlyList<KitchenLineView> Lines);

/// <param name="ServerTimeUtc">For the cards' timers, so a kitchen PC with a wrong clock still counts right.</param>
public sealed record KitchenBoardView(DateTime ServerTimeUtc, IReadOnlyList<KitchenTicketView> Tickets);

/// <summary>The kitchen screen of a branch: what is new, being made, and ready, oldest first.</summary>
public sealed record GetKitchenBoardQuery(Guid BranchId) : IQuery<Result<KitchenBoardView>>;

public sealed class GetKitchenBoardQueryHandler(RestaurantDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope, IClock clock)
    : IQueryHandler<GetKitchenBoardQuery, Result<KitchenBoardView>>
{
    public async ValueTask<Result<KitchenBoardView>> Handle(GetKitchenBoardQuery query, CancellationToken ct)
    {
        if (!(await accessScope.GetBranchScopeAsync(RestaurantPermissions.KitchenOperate, ct)).Covers(query.BranchId))
        {
            return Result.Failure<KitchenBoardView>(Error.NotFound("Branch"));
        }

        var now = clock.UtcNow;
        var readySince = now - KitchenTimings.BoardReady;
        var cancelledSince = now - KitchenTimings.BoardCancelled;
        var oldest = now - KitchenTimings.BoardMaxAge;
        var tickets = await db.KitchenTickets.AsNoTracking()
            .Include(t => t.Lines)
            .Where(t => t.TenantId == currentUser.TenantId && t.BranchId == query.BranchId && t.CreatedAtUtc >= oldest)
            .Where(t => t.Status == KitchenTicketStatus.Queued || t.Status == KitchenTicketStatus.Preparing
                || (t.Status == KitchenTicketStatus.Ready && t.ReadyAtUtc >= readySince)
                || (t.Status == KitchenTicketStatus.Cancelled && t.CancelledAtUtc >= cancelledSince))
            .OrderBy(t => t.CreatedAtUtc)
            .Take(300)
            .ToListAsync(ct);

        return Result.Success(new KitchenBoardView(now, tickets.Select(KitchenViews.From).ToList()));
    }
}

internal static class KitchenViews
{
    public static KitchenTicketView From(KitchenTicket t) => new(
        t.Id, t.SalesOrderId, t.OrderNo, t.RoundNo, t.Channel, t.TableName, t.Notes, t.Status,
        t.CreatedAtUtc, t.StartedAtUtc, t.ReadyAtUtc, t.CancelledAtUtc, Convert.ToBase64String(t.RowVer),
        t.Lines.OrderBy(l => l.LineNumber).Select(l => new KitchenLineView(l.ProductName, l.Quantity, l.Notes)).ToList());
}

/// <summary>A kitchen button on one ticket: Start, Ready, Served, or Back (undo the last tap).</summary>
/// <param name="ExpectedETag">If-Match: when another cook tapped first, the screen reloads instead.</param>
public sealed record KitchenTicketActionCommand(Guid TicketId, string? ExpectedETag, KitchenAction Action) : ICommand<Result<KitchenTicketView>>;

public sealed class KitchenTicketActionCommandHandler(
    RestaurantDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope, IKitchenBoardNotifier notifier, IClock clock)
    : ICommandHandler<KitchenTicketActionCommand, Result<KitchenTicketView>>
{
    public async ValueTask<Result<KitchenTicketView>> Handle(KitchenTicketActionCommand command, CancellationToken ct)
    {
        var ticket = await db.KitchenTickets.Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.TenantId == currentUser.TenantId && t.Id == command.TicketId, ct);
        var grants = await accessScope.GetGrantsAsync(ct);

        // Serving is also for waiters and cashiers; the other buttons are the kitchen's.
        var allowed = ticket is not null && (
            BranchScope.For(grants, RestaurantPermissions.KitchenOperate).Covers(ticket.BranchId) ||
            (command.Action == KitchenAction.Serve && BranchScope.For(grants, RestaurantPermissions.KitchenServe).Covers(ticket.BranchId)));
        if (!allowed)
        {
            return Result.Failure<KitchenTicketView>(Error.NotFound("Ticket"));
        }

        if (command.ExpectedETag is { } etag && !string.Equals(etag.Trim().Trim('"'), Convert.ToBase64String(ticket!.RowVer), StringComparison.Ordinal))
        {
            return Result.Failure<KitchenTicketView>(new Error(ErrorCodes.ConcurrencyConflict, "Someone else just changed this order. The screen will refresh."));
        }

        if (ticket!.Apply(command.Action, currentUser.UserId, clock.UtcNow) is { } error)
        {
            return Result.Failure<KitchenTicketView>(error);
        }

        await db.SaveChangesAsync(ct);
        notifier.BranchChanged(ticket.BranchId);
        return Result.Success(KitchenViews.From(ticket));
    }
}

/// <summary>"Now preparing / Ready to collect" for the hall TV: order numbers only, takeaway and counter orders (not tables).</summary>
public sealed record OrderScreenView(string TenantName, string BranchName, IReadOnlyList<int> Preparing, IReadOnlyList<int> Ready, DateTime ServerTimeUtc);

/// <summary>
/// The customer order screen (no login): the link code alone names the company and branch, like the
/// customer menu. Shows nothing but order numbers.
/// </summary>
public sealed record GetOrderScreenQuery(string Code) : IQuery<Result<OrderScreenView>>;

public sealed class GetOrderScreenQueryHandler(IPublicMenuLinkLookup links, ITenantConnectionFactory connections, IClock clock)
    : IQueryHandler<GetOrderScreenQuery, Result<OrderScreenView>>
{
    public async ValueTask<Result<OrderScreenView>> Handle(GetOrderScreenQuery query, CancellationToken ct)
    {
        if (await links.FindAsync(query.Code, ct, OrderScreenSettings.KeyPrefix) is not { TenantStatus: "Active", Industry: "Restaurant", BranchIsActive: true } link)
        {
            return Result.Failure<OrderScreenView>(Error.NotFound("Screen"));
        }

        var now = clock.UtcNow;

        // Its own connection for the link's tenant: no token, so no request connection; RLS still applies.
        await using var connection = await connections.OpenAsync(link.TenantId, ct);
        var rows = (await connection.QueryAsync<(Guid SalesOrderId, int OrderNo, string Status, DateTime CreatedAtUtc)>(new CommandDefinition(
            """
            SELECT SalesOrderId, OrderNo, Status, CreatedAtUtc
            FROM restaurant.KitchenTicket
            WHERE TenantId = @tenantId AND BranchId = @branchId AND Channel <> 'DineIn' AND CreatedAtUtc >= @since
              AND (Status IN ('Queued', 'Preparing') OR (Status = 'Ready' AND ReadyAtUtc >= @readySince));
            """,
            new { tenantId = link.TenantId, branchId = link.BranchId, since = now - KitchenTimings.BoardMaxAge, readySince = now - KitchenTimings.CustomerScreenReady },
            cancellationToken: ct))).ToList();

        // An order with any round still in the kitchen is "preparing"; it is "ready" once all its shown rounds are.
        var orders = rows.GroupBy(r => r.SalesOrderId).Select(g => new
        {
            g.First().OrderNo,
            Preparing = g.Any(r => r.Status != KitchenTicketStatus.Ready),
            First = g.Min(r => r.CreatedAtUtc),
        }).ToList();

        return Result.Success(new OrderScreenView(
            link.TenantName,
            link.BranchName,
            orders.Where(o => o.Preparing).OrderBy(o => o.First).Select(o => o.OrderNo).Take(40).ToList(),
            orders.Where(o => !o.Preparing).OrderBy(o => o.First).Select(o => o.OrderNo).Take(24).ToList(),
            now));
    }
}
