using Dapper;
using Microsoft.EntityFrameworkCore;
using StockOrder.Infrastructure.Database;
using StockOrder.Infrastructure.NumberSeries;
using StockOrder.Modules.Finance.Contracts;
using StockOrder.Modules.Finance.Domain;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Ids;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Finance.Infrastructure;

internal sealed class FinancePosting(
    FinanceDbContext db,
    IUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    INumberSeriesService numberSeries,
    IIdGenerator idGenerator,
    IClock clock) : IFinancePosting
{
    public async Task<Result> EnsurePeriodOpenAsync(DateOnly businessDate, CancellationToken ct)
    {
        var closed = await db.AccountingPeriods.AnyAsync(
            p => p.TenantId == currentUser.TenantId && p.StartDate <= businessDate && p.EndDate >= businessDate && p.Status == "Closed", ct);
        return closed
            ? Result.Failure(Error.Conflict(ErrorCodes.PeriodClosed, $"The accounting period for {businessDate:yyyy-MM-dd} is closed."))
            : Result.Success();
    }

    public async Task<OpenCashSession?> GetOpenCashSessionAsync(Guid branchId, Guid userId, CancellationToken ct) =>
        await db.CashSessions
            .Where(s => s.TenantId == currentUser.TenantId && s.BranchId == branchId && s.OpenedBy == userId && s.Status == CashSession.Open)
            .OrderByDescending(s => s.OpenedAtUtc)
            .Select(s => new OpenCashSession(s.Id, s.BusinessDate))
            .FirstOrDefaultAsync(ct);

    public async Task DebitCustomerAsync(CustomerLedgerPosting posting, CancellationToken ct) =>
        await InsertLedgerAsync(posting.CustomerId, posting.DocumentType, posting.DocumentId, posting.DocumentNo, posting.Amount, 0m, posting.BusinessDate, ct);

    public async Task<Result<ReceivedPayment>> ReceiveCustomerPaymentAsync(ReceiveCustomerPayment payment, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var method = await db.PaymentMethods.FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Id == payment.PaymentMethodId, ct);
        if (method is null || !method.IsActive)
        {
            return Result.Failure<ReceivedPayment>(Error.Validation("Choose an active payment method."));
        }

        if (payment.Amount <= 0 || payment.Allocations.Sum(a => a.Amount) > payment.Amount)
        {
            return Result.Failure<ReceivedPayment>(Error.Validation("The payment must be positive and cover at least what it settles."));
        }

        var documentNo = await numberSeries.NextAsync(tenantId, FinanceDocumentTypes.Payment, payment.BranchId, ct);
        var paymentId = idGenerator.NewId();
        db.Payments.Add(Payment.ReceiveFromCustomer(
            tenantId, paymentId, documentNo, payment.CustomerId, method.Id, payment.Amount, payment.BusinessDate, payment.BranchId,
            payment.CashSessionId, payment.Reference, payment.ClientDocumentId, payment.SourceDeviceId, currentUser.UserId, clock.UtcNow));
        await db.SaveChangesAsync(ct);

        db.PaymentAllocations.AddRange(payment.Allocations.Select(a =>
            PaymentAllocation.Create(tenantId, idGenerator.NewId(), paymentId, a.DocumentType, a.DocumentId, a.Amount)));
        await db.SaveChangesAsync(ct);

        await InsertLedgerAsync(payment.CustomerId, FinanceDocumentTypes.Payment, paymentId, documentNo, 0m, payment.Amount, payment.BusinessDate, ct);

        return Result.Success(new ReceivedPayment(paymentId, documentNo, method.Name, method.Kind));
    }

    private Task<int> InsertLedgerAsync(
        Guid customerId, string documentType, Guid documentId, string documentNo, decimal debit, decimal credit, DateOnly businessDate, CancellationToken ct) =>
        unitOfWork.Connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT finance.PartyLedgerEntry (TenantId, PartyType, PartyId, DocumentType, DocumentId, DocumentNo, Debit, Credit, BusinessDate, PostedBy)
            VALUES (@tenantId, 'Customer', @customerId, @documentType, @documentId, @documentNo, @debit, @credit, @businessDate, @postedBy);
            """,
            new
            {
                tenantId = currentUser.TenantId,
                customerId,
                documentType,
                documentId,
                documentNo,
                debit,
                credit,
                businessDate = businessDate.ToDateTime(TimeOnly.MinValue),
                postedBy = currentUser.UserId,
            },
            unitOfWork.Transaction,
            cancellationToken: ct));
}
