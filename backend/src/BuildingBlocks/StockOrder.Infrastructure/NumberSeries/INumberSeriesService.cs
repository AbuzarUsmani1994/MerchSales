namespace StockOrder.Infrastructure.NumberSeries;

/// <summary>
/// Issues the next document number inside the caller's own transaction (section 8.9), so a
/// rolled-back post never consumes a number. Numbers are assigned at posting/confirmation,
/// never at draft.
/// </summary>
public interface INumberSeriesService
{
    Task<string> NextAsync(Guid tenantId, string documentType, Guid? branchId, CancellationToken ct);
}
