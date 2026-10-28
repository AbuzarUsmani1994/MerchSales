using StockOrder.SharedKernel.Results;

namespace StockOrder.Modules.Finance.Contracts;

/// <summary>
/// Finance work other modules do inside their own transaction (section 5.4): posting an
/// invoice debits the customer, taking payment credits them and settles the invoice.
/// None of these check the caller's permissions; the calling handler has already.
/// </summary>
public interface IFinancePosting
{
    /// <summary>PERIOD_CLOSED when <paramref name="businessDate"/> falls in a closed accounting period (section 8.11).</summary>
    Task<Result> EnsurePeriodOpenAsync(DateOnly businessDate, CancellationToken ct);

    /// <summary>The caller's open cash session at the branch, if any (section 8.11: it decides the business date).</summary>
    Task<OpenCashSession?> GetOpenCashSessionAsync(Guid branchId, Guid userId, CancellationToken ct);

    /// <summary>
    /// The business date of the branch's oldest open cash session, if any: the trading day is
    /// still running until the till closes, even after midnight (restaurant daily order numbers).
    /// </summary>
    Task<DateOnly?> GetOpenBusinessDateAsync(Guid branchId, CancellationToken ct);

    /// <summary>Customer ledger debit, e.g. for an invoice (append-only finance.PartyLedgerEntry).</summary>
    Task DebitCustomerAsync(CustomerLedgerPosting posting, CancellationToken ct);

    /// <summary>
    /// Records a customer payment: numbered finance.Payment, its allocations, and the customer
    /// ledger credit. Fails with VALIDATION_FAILED for an unknown or inactive method.
    /// </summary>
    Task<Result<ReceivedPayment>> ReceiveCustomerPaymentAsync(ReceiveCustomerPayment payment, CancellationToken ct);
}

public sealed record OpenCashSession(Guid Id, DateOnly BusinessDate);

public sealed record CustomerLedgerPosting(
    Guid CustomerId, string DocumentType, Guid DocumentId, string DocumentNo, decimal Amount, DateOnly BusinessDate);

/// <param name="Allocations">Which documents the payment settles; their sum must not exceed Amount.</param>
public sealed record ReceiveCustomerPayment(
    Guid CustomerId,
    Guid BranchId,
    Guid PaymentMethodId,
    decimal Amount,
    DateOnly BusinessDate,
    Guid? CashSessionId,
    string? Reference,
    IReadOnlyList<PaymentAllocationRequest> Allocations,
    Guid? ClientDocumentId = null,
    Guid? SourceDeviceId = null);

public sealed record PaymentAllocationRequest(string DocumentType, Guid DocumentId, decimal Amount);

public sealed record ReceivedPayment(Guid PaymentId, string DocumentNo, string MethodName, string MethodKind);

public static class FinanceDocumentTypes
{
    public const string Invoice = "Invoice";
    public const string Payment = "Payment";
}
