using StockOrder.SharedKernel.Entities;

namespace StockOrder.Modules.Finance.Domain;

/// <summary>finance.PaymentMethod — seeded from the industry template (CASH, CARD, …).</summary>
public sealed class PaymentMethod : TenantEntity
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;

    /// <summary>Cash, Card, BankTransfer, Wallet, Credit, Cheque. Only Cash counts towards a cash session's expected cash.</summary>
    public string Kind { get; private set; } = string.Empty;

    public bool IsActive { get; private set; } = true;

    private PaymentMethod()
    {
    }
}

/// <summary>finance.CashSession — a cashier's shift at a branch. Its BusinessDate decides which day a sale belongs to.</summary>
public sealed class CashSession : TenantEntity
{
    public const string Open = "Open";
    public const string Closed = "Closed";

    public Guid BranchId { get; private set; }
    public Guid? DeviceId { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public string Status { get; private set; } = Open;
    public Guid OpenedBy { get; private set; }
    public DateTime OpenedAtUtc { get; private set; }
    public decimal OpeningFloat { get; private set; }
    public Guid? ClosedBy { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public decimal? ExpectedCash { get; private set; }
    public decimal? CountedCash { get; private set; }
    public Guid? ClientDocumentId { get; private set; }

    private CashSession()
    {
    }

    public static CashSession Start(Guid tenantId, Guid id, Guid branchId, DateOnly businessDate, Guid openedBy, DateTime now, decimal openingFloat) =>
        new()
        {
            TenantId = tenantId,
            Id = id,
            BranchId = branchId,
            BusinessDate = businessDate,
            OpenedBy = openedBy,
            OpenedAtUtc = now,
            OpeningFloat = openingFloat,
        };

    public void Close(Guid closedBy, DateTime now, decimal expectedCash, decimal countedCash)
    {
        Status = Closed;
        ClosedBy = closedBy;
        ClosedAtUtc = now;
        ExpectedCash = expectedCash;
        CountedCash = countedCash;
    }
}

/// <summary>finance.Payment — money in from a customer (or out to a supplier, later).</summary>
public sealed class Payment : TenantEntity
{
    public string? DocumentNo { get; private set; }
    public string Direction { get; private set; } = "In";
    public string PartyType { get; private set; } = "Customer";
    public Guid PartyId { get; private set; }
    public Guid PaymentMethodId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "PKR";
    public DateOnly PaymentDate { get; private set; }
    public string? Reference { get; private set; }
    public string Status { get; private set; } = "Posted";
    public Guid BranchId { get; private set; }
    public Guid? CashSessionId { get; private set; }
    public Guid? CollectedByUserId { get; private set; }
    public Guid? ClientDocumentId { get; private set; }
    public Guid? SourceDeviceId { get; private set; }
    public string? Notes { get; private set; }
    public Guid? PostedBy { get; private set; }
    public DateTime PostedAtUtc { get; private set; }

    private Payment()
    {
    }

    public static Payment ReceiveFromCustomer(
        Guid tenantId, Guid id, string documentNo, Guid customerId, Guid methodId, decimal amount, DateOnly date,
        Guid branchId, Guid? cashSessionId, string? reference, Guid? clientDocumentId, Guid? sourceDeviceId, Guid postedBy, DateTime now) =>
        new()
        {
            TenantId = tenantId,
            Id = id,
            DocumentNo = documentNo,
            PartyId = customerId,
            PaymentMethodId = methodId,
            Amount = amount,
            PaymentDate = date,
            BranchId = branchId,
            CashSessionId = cashSessionId,
            Reference = reference,
            ClientDocumentId = clientDocumentId,
            SourceDeviceId = sourceDeviceId,
            CollectedByUserId = postedBy,
            PostedBy = postedBy,
            PostedAtUtc = now,
        };
}

/// <summary>finance.PaymentAllocation — which invoice a payment settles.</summary>
public sealed class PaymentAllocation : TenantEntity
{
    public Guid PaymentId { get; private set; }
    public string DocumentType { get; private set; } = string.Empty;
    public Guid DocumentId { get; private set; }
    public decimal Amount { get; private set; }

    private PaymentAllocation()
    {
    }

    public static PaymentAllocation Create(Guid tenantId, Guid id, Guid paymentId, string documentType, Guid documentId, decimal amount) =>
        new() { TenantId = tenantId, Id = id, PaymentId = paymentId, DocumentType = documentType, DocumentId = documentId, Amount = amount };
}

/// <summary>finance.AccountingPeriod — posting into a Closed period is refused (PERIOD_CLOSED).</summary>
public sealed class AccountingPeriod : TenantEntity
{
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public string Status { get; private set; } = "Open";

    private AccountingPeriod()
    {
    }
}
