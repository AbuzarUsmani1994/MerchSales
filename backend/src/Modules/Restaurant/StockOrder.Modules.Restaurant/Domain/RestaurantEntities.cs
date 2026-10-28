using StockOrder.SharedKernel.Entities;
using StockOrder.SharedKernel.Errors;

namespace StockOrder.Modules.Restaurant.Domain;

/// <summary>restaurant.DiningArea — a part of a branch with tables (Main hall, Family hall, Roof).</summary>
public sealed class DiningArea : AuditedTenantEntity
{
    public Guid BranchId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    private DiningArea()
    {
    }

    private DiningArea(Guid tenantId, Guid id, DateTime now, Guid? by)
        : base(tenantId, id, now, by)
    {
    }

    public static DiningArea Create(Guid tenantId, Guid id, Guid branchId, string name, int sortOrder, DateTime now, Guid? by) =>
        new(tenantId, id, now, by) { BranchId = branchId, Name = name, SortOrder = sortOrder };

    public void Update(string name, int sortOrder, bool isActive, DateTime now, Guid? by)
    {
        Name = name;
        SortOrder = sortOrder;
        IsActive = isActive;
        MarkUpdated(now, by);
    }
}

/// <summary>restaurant.DiningTable — a table staff call by its name ("4", "T12").</summary>
public sealed class DiningTable : AuditedTenantEntity
{
    public Guid BranchId { get; private set; }
    public Guid AreaId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int Seats { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    private DiningTable()
    {
    }

    private DiningTable(Guid tenantId, Guid id, DateTime now, Guid? by)
        : base(tenantId, id, now, by)
    {
    }

    public static DiningTable Create(Guid tenantId, Guid id, Guid branchId, Guid areaId, string name, int seats, int sortOrder, DateTime now, Guid? by) =>
        new(tenantId, id, now, by) { BranchId = branchId, AreaId = areaId, Name = name, Seats = seats, SortOrder = sortOrder };

    public void Update(Guid areaId, string name, int seats, int sortOrder, bool isActive, DateTime now, Guid? by)
    {
        AreaId = areaId;
        Name = name;
        Seats = seats;
        SortOrder = sortOrder;
        IsActive = isActive;
        MarkUpdated(now, by);
    }
}

public static class KitchenTicketStatus
{
    public const string Queued = "Queued";
    public const string Preparing = "Preparing";
    public const string Ready = "Ready";
    public const string Served = "Served";
    public const string Cancelled = "Cancelled";

    public static readonly IReadOnlyList<string> InKitchen = [Queued, Preparing, Ready];
}

/// <summary>The kitchen's buttons: Start, Ready and Served move a ticket on; Back undoes a mistaken tap.</summary>
public enum KitchenAction
{
    Start,
    Ready,
    Serve,
    Back,
}

/// <summary>
/// restaurant.KitchenTicket — one round of an order on the kitchen screen:
/// Queued → Preparing → Ready → Served, or Cancelled with its order.
/// </summary>
public sealed class KitchenTicket : AuditedTenantEntity
{
    private readonly List<KitchenTicketLine> _lines = [];

    public Guid SalesOrderId { get; private set; }
    public Guid BranchId { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public int OrderNo { get; private set; }
    public int RoundNo { get; private set; }
    public string Channel { get; private set; } = string.Empty;
    public string? TableName { get; private set; }
    public string? Notes { get; private set; }
    public string Status { get; private set; } = KitchenTicketStatus.Queued;
    public DateTime? StartedAtUtc { get; private set; }
    public Guid? StartedBy { get; private set; }
    public DateTime? ReadyAtUtc { get; private set; }
    public Guid? ReadyBy { get; private set; }
    public DateTime? ServedAtUtc { get; private set; }
    public Guid? ServedBy { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }

    public IReadOnlyList<KitchenTicketLine> Lines => _lines;

    private KitchenTicket()
    {
    }

    private KitchenTicket(Guid tenantId, Guid id, DateTime now, Guid? by)
        : base(tenantId, id, now, by)
    {
    }

    public static KitchenTicket Create(
        Guid tenantId, Guid id, Guid orderId, Guid branchId, DateOnly businessDate, int orderNo, int roundNo, string channel,
        string? tableName, string? notes, DateTime now, Guid? by) =>
        new(tenantId, id, now, by)
        {
            SalesOrderId = orderId,
            BranchId = branchId,
            BusinessDate = businessDate,
            OrderNo = orderNo,
            RoundNo = roundNo,
            Channel = channel,
            TableName = tableName,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        };

    public void AddLine(Guid id, Guid orderLineId, Guid productId, string productName, decimal quantity, string? notes) =>
        _lines.Add(KitchenTicketLine.Create(TenantId, id, Id, _lines.Count + 1, orderLineId, productId, productName, quantity, notes));

    /// <summary>INVALID_STATE_TRANSITION when the button doesn't fit the ticket's status (someone else tapped first).</summary>
    public Error? Apply(KitchenAction action, Guid userId, DateTime now)
    {
        switch (action, Status)
        {
            case (KitchenAction.Start, KitchenTicketStatus.Queued):
                Status = KitchenTicketStatus.Preparing;
                StartedAtUtc = now;
                StartedBy = userId;
                break;
            case (KitchenAction.Ready, KitchenTicketStatus.Queued or KitchenTicketStatus.Preparing):
                StartedAtUtc ??= now;
                StartedBy ??= userId;
                Status = KitchenTicketStatus.Ready;
                ReadyAtUtc = now;
                ReadyBy = userId;
                break;
            case (KitchenAction.Serve, KitchenTicketStatus.Queued or KitchenTicketStatus.Preparing or KitchenTicketStatus.Ready):
                Status = KitchenTicketStatus.Served;
                ReadyAtUtc ??= now;
                ServedAtUtc = now;
                ServedBy = userId;
                break;
            case (KitchenAction.Back, KitchenTicketStatus.Preparing):
                Status = KitchenTicketStatus.Queued;
                StartedAtUtc = null;
                StartedBy = null;
                break;
            case (KitchenAction.Back, KitchenTicketStatus.Ready):
                Status = KitchenTicketStatus.Preparing;
                ReadyAtUtc = null;
                ReadyBy = null;
                break;
            default:
                return Error.Conflict(ErrorCodes.InvalidStateTransition, Status switch
                {
                    KitchenTicketStatus.Served => $"Order #{OrderNo} was already served.",
                    KitchenTicketStatus.Cancelled => $"Order #{OrderNo} was cancelled.",
                    _ => $"Order #{OrderNo} is {Status.ToLowerInvariant()} already.",
                });
        }

        MarkUpdated(now, userId);
        return null;
    }

    /// <summary>The order was cancelled: a ticket not yet served comes off the screen (shown crossed out for a while).</summary>
    public void Cancel(DateTime now, Guid? by)
    {
        if (Status is KitchenTicketStatus.Served or KitchenTicketStatus.Cancelled)
        {
            return;
        }

        Status = KitchenTicketStatus.Cancelled;
        CancelledAtUtc = now;
        MarkUpdated(now, by);
    }
}

/// <summary>restaurant.KitchenTicketLine — an item of a round, with its name as it was when sent. Never changed.</summary>
public sealed class KitchenTicketLine : TenantEntity
{
    public Guid TicketId { get; private set; }
    public int LineNumber { get; private set; }
    public Guid SalesOrderLineId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public string? Notes { get; private set; }

    private KitchenTicketLine()
    {
    }

    internal static KitchenTicketLine Create(
        Guid tenantId, Guid id, Guid ticketId, int lineNumber, Guid orderLineId, Guid productId, string productName, decimal quantity, string? notes) =>
        new()
        {
            TenantId = tenantId,
            Id = id,
            TicketId = ticketId,
            LineNumber = lineNumber,
            SalesOrderLineId = orderLineId,
            ProductId = productId,
            ProductName = productName,
            Quantity = quantity,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        };
}
