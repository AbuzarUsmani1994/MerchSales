using Microsoft.Data.SqlClient;
using StockOrder.Infrastructure.Database;

namespace StockOrder.Infrastructure.NumberSeries;

internal sealed class NumberSeriesService(IUnitOfWork unitOfWork) : INumberSeriesService
{
    public async Task<string> NextAsync(Guid tenantId, string documentType, Guid? branchId, CancellationToken ct)
    {
        if (branchId is { } specificBranch)
        {
            var issued = await TryIssueAsync(tenantId, documentType, specificBranch, ct);
            if (issued is not null)
            {
                return issued;
            }
        }

        return await TryIssueAsync(tenantId, documentType, null, ct)
            ?? throw new InvalidOperationException(
                $"No number series configured for TenantId={tenantId}, DocumentType={documentType}, BranchId={branchId}.");
    }

    private async Task<string?> TryIssueAsync(Guid tenantId, string documentType, Guid? branchId, CancellationToken ct)
    {
        await using var command = unitOfWork.Connection.CreateCommand();
        command.Transaction = unitOfWork.Transaction;
        command.CommandText = """
            UPDATE platform.NumberSeries
            SET NextNumber = NextNumber + 1
            OUTPUT deleted.Prefix, deleted.NextNumber, deleted.PaddingLength
            WHERE TenantId = @tenantId AND DocumentType = @documentType
              AND ((@branchId IS NULL AND BranchId IS NULL) OR BranchId = @branchId);
            """;
        command.Parameters.Add(new SqlParameter("@tenantId", tenantId));
        command.Parameters.Add(new SqlParameter("@documentType", documentType));
        command.Parameters.Add(new SqlParameter("@branchId", (object?)branchId ?? DBNull.Value));

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var prefix = reader.GetString(0);
        var number = reader.GetInt64(1);
        var padding = reader.GetByte(2);

        return prefix + number.ToString().PadLeft(padding, '0');
    }
}
