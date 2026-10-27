using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Finance.Contracts;
using StockOrder.SharedKernel.Context;

namespace StockOrder.Modules.Finance.Infrastructure;

internal sealed class PaymentQueries(FinanceDbContext db, ICurrentUserContext currentUser) : IPaymentQueries
{
    public async Task<IReadOnlyList<DocumentPayment>> ForDocumentAsync(string documentType, Guid documentId, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        return await (
                from a in db.PaymentAllocations
                join p in db.Payments on a.PaymentId equals p.Id
                join m in db.PaymentMethods on p.PaymentMethodId equals m.Id
                where a.TenantId == tenantId && a.DocumentType == documentType && a.DocumentId == documentId && p.Status == "Posted"
                orderby p.PostedAtUtc
                select new DocumentPayment(p.Id, p.DocumentNo, m.Name, m.Kind, a.Amount, p.Reference, p.PostedBy, p.PostedAtUtc))
            .ToListAsync(ct);
    }
}
