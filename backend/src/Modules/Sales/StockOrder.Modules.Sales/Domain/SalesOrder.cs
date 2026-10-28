using StockOrder.SharedKernel.Entities;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Numbers;

namespace StockOrder.Modules.Sales.Domain;

public static class SalesOrderStatus
{
    public const string Draft = "Draft";
    public const string Submitted = "Submitted";
    public const string OnHold = "OnHold";
    public const string Confirmed = "Confirmed";
    public const string Fulfilling = "Fulfilling";
    public const string Dispatched = "Dispatched";
    public const string Delivered = "Delivered";
    public const string Invoiced = "Invoiced";
    public const string Paid = "Paid";
    public const string Closed = "Closed";
    public const string Cancelled = "Cancelled";

    /// <summary>The section 8.6 table: which status each one may move to.</summary>
    private static readonly Dictionary<string, string[]> Allowed = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [Draft] = [Submitted, Cancelled],
        [Submitted] = [Confirmed, OnHold, Cancelled],
        [OnHold] = [Confirmed, Cancelled],
        [Confirmed] = [Fulfilling, Dispatched, Cancelled],
        [Fulfilling] = [Dispatched, Cancelled],
        [Dispatched] = [Delivered, Invoiced],
        [Delivered] = [Invoiced],
        [Invoiced] = [Paid],
        [Paid] = [Closed],
        [Closed] = [],
        [Cancelled] = [],
    };

    public static bool CanMove(string from, string to) => Allowed.TryGetValue(from, out var next) && next.Contains(to);
}

public static class SalesChannels
{
    public const string BackOffice = "BackOffice";
    public const string Counter = "Counter";
    public const string DineIn = "DineIn";
    public const string Takeaway = "Takeaway";
    public const string Delivery = "Delivery";
    public const string FieldBooking = "FieldBooking";

    /// <summary>
    /// Channels an order can be taken on today. FieldBooking (a rep ordering for a pharmacy)
    /// needs customers and stock, which come with the pharma pack.
    /// </summary>
    public static readonly IReadOnlySet<string> Supported = new HashSet<string>([BackOffice, DineIn, Takeaway, Delivery], StringComparer.Ordinal);
}

/// <summary>sales.SalesOrder — one state machine for every channel (section 8.6).</summary>
public sealed class SalesOrder : AuditedTenantEntity
{
    private readonly List<SalesOrderLine> _lines = [];
    private readonly List<SalesOrderStatusChange> _statusHistory = [];

    public string? DocumentNo { get; private set; }
    public string? DeviceDocumentNo { get; private set; }
    public string Channel { get; private set; } = SalesChannels.BackOffice;
    public Guid CustomerId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public DateTime OrderDate { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public string Status { get; private set; } = SalesOrderStatus.Draft;
    public string? HoldReason { get; private set; }
    public Guid? PriceListId { get; private set; }
    public string Currency { get; private set; } = "PKR";
    public decimal SubTotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal ChargesTotal { get; private set; }
    public decimal GrandTotal { get; private set; }
    public decimal PaidAmount { get; private set; }
    public string BackOrderPolicy { get; private set; } = "Reject";
    public string? Notes { get; private set; }

    /// <summary>A member's phone the waiter noted on the order (015_Loyalty), for the cashier.</summary>
    public string? LoyaltyPhone { get; private set; }
    public Guid? SalesRepUserId { get; private set; }
    public Guid? ClientDocumentId { get; private set; }
    public Guid? SourceDeviceId { get; private set; }
    public Guid? CashSessionId { get; private set; }
    public DateTime? ConfirmedAtUtc { get; private set; }
    public DateTime? DispatchedAtUtc { get; private set; }
    public DateTime? DeliveredAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancelNotes { get; private set; }
    public int Version { get; private set; } = 1;

    // Restaurant pack (016_RestaurantPack).

    /// <summary>The table the order was taken at; kept after the order closes, for reports.</summary>
    public Guid? TableId { get; private set; }

    /// <summary>Same as <see cref="TableId"/> while the order is open, null after: the database allows one open order per table.</summary>
    public Guid? OpenTableId { get; private set; }
    public int? Covers { get; private set; }
    public Guid? ServerUserId { get; private set; }

    /// <summary>The short daily number (#23) for the kitchen and the customer; null outside restaurants.</summary>
    public int? OrderNo { get; private set; }
    public DateTime? BillRequestedAtUtc { get; private set; }

    public IReadOnlyList<SalesOrderLine> Lines => _lines;
    public IReadOnlyList<SalesOrderStatusChange> StatusHistory => _statusHistory;

    private SalesOrder()
    {
    }

    /// <summary>A new Draft order; prices are a snapshot taken now (section 8.6 "create").</summary>
    public static SalesOrder CreateDraft(
        Guid tenantId, Guid id, string channel, Guid customerId, Guid branchId, Guid warehouseId, DateOnly businessDate,
        string? notes, Guid? clientDocumentId, Guid? sourceDeviceId, string? deviceDocumentNo, Guid createdBy, DateTime now)
    {
        var order = new SalesOrder
        {
            TenantId = tenantId,
            Id = id,
            Channel = channel,
            CustomerId = customerId,
            BranchId = branchId,
            WarehouseId = warehouseId,
            OrderDate = now,
            BusinessDate = businessDate,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            SalesRepUserId = createdBy,
            ClientDocumentId = clientDocumentId,
            SourceDeviceId = sourceDeviceId,
            DeviceDocumentNo = deviceDocumentNo,
            CreatedAtUtc = now,
            CreatedBy = createdBy,
        };
        order._statusHistory.Add(SalesOrderStatusChange.Create(tenantId, id, null, SalesOrderStatus.Draft, createdBy, now, null));
        return order;
    }

    public SalesOrderLine AddLine(Guid lineId, Guid productId, Guid uomId, decimal quantity, decimal qtyBase, decimal unitPrice,
        Guid? taxCodeId, decimal taxRatePercent, string? notes)
    {
        var amounts = LineMath.Compute(quantity, unitPrice, discountPercent: 0m, taxRatePercent);
        var line = SalesOrderLine.Create(
            TenantId, lineId, Id, _lines.Count + 1, productId, uomId, quantity, qtyBase, unitPrice, amounts, taxCodeId, taxRatePercent, notes);
        _lines.Add(line);
        RecalculateTotals();
        return line;
    }

    /// <summary>Moves the order along the section 8.6 table, recording the change; INVALID_STATE_TRANSITION otherwise.</summary>
    public Error? MoveTo(string to, Guid? userId, DateTime now, string? reason = null)
    {
        if (!SalesOrderStatus.CanMove(Status, to))
        {
            return Error.Conflict(ErrorCodes.InvalidStateTransition, $"An order that is {Status} can't become {to}.");
        }

        if (to == SalesOrderStatus.Closed && DeliveredAtUtc is null)
        {
            return Error.Conflict(ErrorCodes.InvalidStateTransition, "Only a paid order that was delivered can be closed.");
        }

        _statusHistory.Add(SalesOrderStatusChange.Create(TenantId, Id, Status, to, userId, now, reason));
        Status = to;
        MarkUpdated(now, userId);
        if (to is SalesOrderStatus.Closed or SalesOrderStatus.Cancelled)
        {
            OpenTableId = null;
        }

        switch (to)
        {
            case SalesOrderStatus.Confirmed:
                ConfirmedAtUtc = now;
                break;
            case SalesOrderStatus.Dispatched:
                DispatchedAtUtc = now;
                foreach (var line in _lines)
                {
                    line.Fulfil();
                }

                break;
            case SalesOrderStatus.Delivered:
                DeliveredAtUtc = now;
                break;
            case SalesOrderStatus.Cancelled:
                CancelledAtUtc = now;
                CancelNotes = reason;
                foreach (var line in _lines)
                {
                    line.Cancel();
                }

                break;
        }

        return null;
    }

    /// <summary>A dine-in order at a table; the table stays taken until the order is paid or cancelled.</summary>
    public void SeatAt(Guid tableId, int? covers, Guid serverUserId)
    {
        TableId = tableId;
        OpenTableId = tableId;
        Covers = covers;
        ServerUserId = serverUserId;
    }

    public void AssignOrderNo(int orderNo) => OrderNo = orderNo;

    /// <summary>
    /// The guests order more: allowed while the order is Confirmed (section 8.6 amendments; nothing
    /// is reserved yet, so there is nothing to re-reserve). A bill asked for earlier no longer
    /// shows everything, so the request is cleared.
    /// </summary>
    public Error? StartAmendment(Guid userId, DateTime now)
    {
        if (Status != SalesOrderStatus.Confirmed)
        {
            return Error.Conflict(ErrorCodes.InvalidStateTransition, $"Items can only be added to a confirmed order; this one is {Status}.");
        }

        Version += 1;
        BillRequestedAtUtc = null;
        MarkUpdated(now, userId);
        return null;
    }

    public Error? RequestBill(Guid userId, DateTime now)
    {
        if (Status != SalesOrderStatus.Confirmed)
        {
            return Error.Conflict(ErrorCodes.InvalidStateTransition, $"The bill can only be asked for on a confirmed order; this one is {Status}.");
        }

        BillRequestedAtUtc ??= now;
        MarkUpdated(now, userId);
        return null;
    }

    /// <summary>The DocumentNo is issued at confirmation, never at draft (section 8.9).</summary>
    public void AssignDocumentNo(string documentNo) => DocumentNo = documentNo;

    public void AttachCashSession(Guid cashSessionId) => CashSessionId = cashSessionId;

    public void RecordPayment(decimal amount) => PaidAmount += amount;

    public void NoteLoyaltyPhone(string? phone) => LoyaltyPhone = phone;

    /// <summary>The order belongs to a member from here on (their customer record, ledger and history).</summary>
    public void AssignCustomer(Guid customerId) => CustomerId = customerId;

    /// <summary>
    /// A fixed amount off the whole order (a visit reward), spread over the lines in proportion
    /// to their amounts so every line still follows the section 8.7 maths; the last line takes
    /// the rounding remainder. The caller checks the order is big enough.
    /// </summary>
    public void ApplyOrderDiscount(decimal amount)
    {
        var open = _lines.Where(l => l.Status != SalesOrderLineStatus.Cancelled).ToList();
        var bases = open.Select(l => l.Gross - l.DiscountAmount).ToList();
        var total = bases.Sum();
        if (amount <= 0 || total <= 0)
        {
            return;
        }

        amount = Math.Min(amount, total);
        var left = amount;
        for (var i = 0; i < open.Count; i++)
        {
            var share = i == open.Count - 1 ? left : Math.Min(left, Rounding.Money(amount * bases[i] / total));
            open[i].AddDiscount(share);
            left -= share;
        }

        RecalculateTotals();
    }

    private void RecalculateTotals()
    {
        // Sums of the rounded line values (section 8.7); gross = total − tax + discount.
        SubTotal = _lines.Sum(l => l.LineTotal - l.TaxAmount + l.DiscountAmount);
        DiscountTotal = _lines.Sum(l => l.DiscountAmount);
        TaxTotal = _lines.Sum(l => l.TaxAmount);
        GrandTotal = SubTotal - DiscountTotal + TaxTotal + ChargesTotal;
    }
}

public static class SalesOrderLineStatus
{
    public const string Open = "Open";
    public const string Fulfilled = "Fulfilled";
    public const string Cancelled = "Cancelled";
}

/// <summary>sales.SalesOrderLine. Quantity is in the line's UoM; all stock logic uses QtyBase (section 8.1).</summary>
public sealed class SalesOrderLine : TenantEntity
{
    public Guid SalesOrderId { get; private set; }
    public int LineNumber { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid UomId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal QtyBase { get; private set; }
    public decimal FulfilledQtyBase { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal DiscountPercent { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public Guid? TaxCodeId { get; private set; }
    public decimal TaxRatePercent { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal LineTotal { get; private set; }
    public string Status { get; private set; } = SalesOrderLineStatus.Open;
    public string? Notes { get; private set; }

    private SalesOrderLine()
    {
    }

    internal static SalesOrderLine Create(
        Guid tenantId, Guid id, Guid orderId, int lineNumber, Guid productId, Guid uomId, decimal quantity, decimal qtyBase,
        decimal unitPrice, LineAmounts amounts, Guid? taxCodeId, decimal taxRatePercent, string? notes) =>
        new()
        {
            TenantId = tenantId,
            Id = id,
            SalesOrderId = orderId,
            LineNumber = lineNumber,
            ProductId = productId,
            UomId = uomId,
            Quantity = quantity,
            QtyBase = qtyBase,
            UnitPrice = unitPrice,
            DiscountAmount = amounts.DiscountAmount,
            TaxCodeId = taxCodeId,
            TaxRatePercent = taxRatePercent,
            TaxAmount = amounts.TaxAmount,
            LineTotal = amounts.LineTotal,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        };

    internal decimal Gross => Rounding.Money(Quantity * UnitPrice);

    internal void AddDiscount(decimal amount)
    {
        DiscountAmount += amount;
        TaxAmount = Rounding.Money((Gross - DiscountAmount) * TaxRatePercent / 100m);
        LineTotal = Gross - DiscountAmount + TaxAmount;
    }

    internal void Fulfil()
    {
        FulfilledQtyBase = QtyBase;
        Status = SalesOrderLineStatus.Fulfilled;
    }

    internal void Cancel() => Status = SalesOrderLineStatus.Cancelled;
}

/// <summary>sales.SalesOrderStatusHistory — one row per transition (section 8.6).</summary>
public sealed class SalesOrderStatusChange
{
    public long Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SalesOrderId { get; private set; }
    public string? FromStatus { get; private set; }
    public string ToStatus { get; private set; } = string.Empty;
    public Guid? ChangedBy { get; private set; }
    public DateTime ChangedAtUtc { get; private set; }
    public string? Reason { get; private set; }

    private SalesOrderStatusChange()
    {
    }

    internal static SalesOrderStatusChange Create(Guid tenantId, Guid orderId, string? from, string to, Guid? by, DateTime at, string? reason) =>
        new() { TenantId = tenantId, SalesOrderId = orderId, FromStatus = from, ToStatus = to, ChangedBy = by, ChangedAtUtc = at, Reason = reason };
}
