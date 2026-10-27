namespace StockOrder.Modules.Finance.Contracts;

/// <summary>Read-only payment lookups other modules use, e.g. Sales printing a receipt.</summary>
public interface IPaymentQueries
{
    /// <summary>Posted payments allocated to a document (e.g. an invoice), oldest first.</summary>
    Task<IReadOnlyList<DocumentPayment>> ForDocumentAsync(string documentType, Guid documentId, CancellationToken ct);
}

/// <param name="Amount">The part of the payment allocated to this document.</param>
public sealed record DocumentPayment(
    Guid PaymentId, string? DocumentNo, string MethodName, string MethodKind, decimal Amount, string? Reference, Guid? PostedBy, DateTime PostedAtUtc);
