using StockOrder.SharedKernel.Entities;

namespace StockOrder.Modules.Sales.Domain;

/// <summary>sales.Invoice — gap-free numbered at posting (section 8.9); posting debits the customer.</summary>
public sealed class Invoice : TenantEntity
{
    private readonly List<InvoiceLine> _lines = [];

    public string DocumentNo { get; private set; } = string.Empty;
    public Guid? SalesOrderId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid BranchId { get; private set; }
    public DateOnly InvoiceDate { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public string Status { get; private set; } = "Posted";
    public string Currency { get; private set; } = "PKR";
    public decimal SubTotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal ChargesTotal { get; private set; }
    public decimal GrandTotal { get; private set; }
    public decimal AmountPaid { get; private set; }
    public Guid? PostedBy { get; private set; }
    public DateTime PostedAtUtc { get; private set; }

    public IReadOnlyList<InvoiceLine> Lines => _lines;

    private Invoice()
    {
    }

    /// <summary>Invoices the whole order; product names are snapshotted onto the lines.</summary>
    public static Invoice ForOrder(
        Guid tenantId, Guid id, string documentNo, SalesOrder order, DateOnly invoiceDate,
        IReadOnlyDictionary<Guid, string> productNames, Func<Guid> newId, Guid postedBy, DateTime now)
    {
        var invoice = new Invoice
        {
            TenantId = tenantId,
            Id = id,
            DocumentNo = documentNo,
            SalesOrderId = order.Id,
            CustomerId = order.CustomerId,
            BranchId = order.BranchId,
            InvoiceDate = invoiceDate,
            SubTotal = order.SubTotal,
            DiscountTotal = order.DiscountTotal,
            TaxTotal = order.TaxTotal,
            ChargesTotal = order.ChargesTotal,
            GrandTotal = order.GrandTotal,
            PostedBy = postedBy,
            PostedAtUtc = now,
        };

        foreach (var line in order.Lines.Where(l => l.Status != SalesOrderLineStatus.Cancelled))
        {
            invoice._lines.Add(InvoiceLine.FromOrderLine(tenantId, newId(), id, invoice._lines.Count + 1, line, productNames.GetValueOrDefault(line.ProductId, "Item")));
        }

        return invoice;
    }

    public void RecordPayment(decimal amount) => AmountPaid += amount;
}

/// <summary>sales.InvoiceLine.</summary>
public sealed class InvoiceLine : TenantEntity
{
    public Guid InvoiceId { get; private set; }
    public int LineNumber { get; private set; }
    public Guid? SalesOrderLineId { get; private set; }
    public Guid ProductId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public Guid UomId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal QtyBase { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public Guid? TaxCodeId { get; private set; }
    public decimal TaxRatePercent { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal LineTotal { get; private set; }

    private InvoiceLine()
    {
    }

    internal static InvoiceLine FromOrderLine(Guid tenantId, Guid id, Guid invoiceId, int lineNumber, SalesOrderLine line, string description) =>
        new()
        {
            TenantId = tenantId,
            Id = id,
            InvoiceId = invoiceId,
            LineNumber = lineNumber,
            SalesOrderLineId = line.Id,
            ProductId = line.ProductId,
            Description = description.Length > 300 ? description[..300] : description,
            UomId = line.UomId,
            Quantity = line.Quantity,
            QtyBase = line.QtyBase,
            UnitPrice = line.UnitPrice,
            DiscountAmount = line.DiscountAmount,
            TaxCodeId = line.TaxCodeId,
            TaxRatePercent = line.TaxRatePercent,
            TaxAmount = line.TaxAmount,
            LineTotal = line.LineTotal,
        };
}

/// <summary>sales.Customer — read here for the walk-in customer and price lists; managed in a later step.</summary>
public sealed class Customer : TenantEntity
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public Guid? PriceListId { get; private set; }
    public bool IsWalkIn { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Customer()
    {
    }
}
