using System.Text.Json;
using Dapper;
using Mediator;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Catalog.Contracts;
using StockOrder.Modules.Sales.Application;
using StockOrder.Modules.Sales.Domain;
using StockOrder.Modules.Sync.Contracts;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Sales.Infrastructure;

/// <summary>
/// Section 12 command "CreateSalesOrder": an order taken on the phone, possibly offline. Payload
/// <c>{ id, branchId, channel?, notes?, deviceDocumentNo?, lines: [{ productId, quantity, notes? }] }</c>
/// where <c>id</c> is the phone's own id for the order. Runs the same command as POST /sales/orders.
/// </summary>
internal sealed class CreateSalesOrderSyncHandler(IMediator mediator) : ISyncCommandHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string CommandType => "CreateSalesOrder";

    public async Task<SyncCommandOutcome> ApplyAsync(SyncCommandContext context, JsonElement payload, CancellationToken ct)
    {
        Payload? body;
        try
        {
            body = payload.Deserialize<Payload>(Json);
        }
        catch (JsonException)
        {
            body = null;
        }

        if (body is not { Id: var id, BranchId: var branchId } || id == Guid.Empty || branchId == Guid.Empty || body.Lines is null)
        {
            return SyncCommandOutcome.Rejected(SyncErrorCodes.InvalidPayload, "Expected { id, branchId, lines: [{ productId, quantity }] }.");
        }

        var result = await mediator.Send(new CreateSalesOrderCommand(
            branchId,
            body.Channel ?? SalesChannels.DineIn,
            body.Notes,
            body.Lines.Select(l => new CreateSalesOrderLine(l.ProductId, l.Quantity, l.Notes)).ToList(),
            ClientDocumentId: id,
            SourceDeviceId: context.DeviceId,
            DeviceDocumentNo: body.DeviceDocumentNo), ct);

        if (result.IsFailure)
        {
            // Validation, permission and pricing problems won't fix themselves on a retry: final.
            return SyncCommandOutcome.Rejected(result.Error.Code, result.Error.Message);
        }

        var order = result.Value;
        return SyncCommandOutcome.Applied(new
        {
            serverIds = new { salesOrder = order.Id },
            documentNo = order.DocumentNo,
            status = order.Status,
            grandTotal = order.GrandTotal,
        });
    }

    private sealed record Payload(Guid Id, Guid BranchId, string? Channel, string? Notes, string? DeviceDocumentNo, List<PayloadLine>? Lines);

    private sealed record PayloadLine(Guid ProductId, decimal Quantity, string? Notes);
}

/// <summary>
/// Section 12 entity "openOrders": orders of the user's branches from the last 7 days, with their
/// lines, so the phone shows what the cashier did with them (Confirmed → Closed / Cancelled).
/// </summary>
internal sealed class OpenOrdersSyncSource(
    IUnitOfWork unitOfWork, ICurrentUserContext currentUser, IAccessScope accessScope, ISalesCatalog catalog, IClock clock)
    : ISyncSource
{
    public string Entity => "openOrders";

    public async Task<SyncEntityPage> PullAsync(SyncPullRequest request, CancellationToken ct)
    {
        var scope = BranchScope.For(await accessScope.GetGrantsAsync(ct), SalesPermissions.View);
        var parameters = new DynamicParameters();
        parameters.Add("tenantId", currentUser.TenantId);
        parameters.Add("limit", request.Limit);
        parameters.Add("after", RowVersionToken.ToBinary(RowVersionToken.Parse(request.Token)), System.Data.DbType.Binary, size: 8);
        parameters.Add("upper", RowVersionToken.ToBinary(request.UpperRowVersion), System.Data.DbType.Binary, size: 8);
        parameters.Add("since", clock.UtcNow.AddDays(-7));
        parameters.Add("allBranches", scope.AllBranches);
        parameters.Add("branchIds", scope.BranchIds.Count > 0 ? scope.BranchIds.ToArray() : [Guid.Empty]);

        var orders = (await unitOfWork.Connection.QueryAsync<OrderRow>(new CommandDefinition(
            """
            SELECT TOP (@limit) Id, DocumentNo, ClientDocumentId, Status, Channel, BranchId, Notes, GrandTotal, PaidAmount,
                   OrderDate, SalesRepUserId, RowVer
            FROM sales.SalesOrder
            WHERE TenantId = @tenantId AND RowVer > @after AND RowVer < @upper AND OrderDate >= @since
              AND (@allBranches = 1 OR BranchId IN @branchIds)
            ORDER BY RowVer;
            """, parameters, unitOfWork.Transaction, cancellationToken: ct))).ToList();

        var lines = orders.Count == 0
            ? Enumerable.Empty<LineRow>().ToLookup(l => l.SalesOrderId)
            : (await unitOfWork.Connection.QueryAsync<LineRow>(new CommandDefinition(
                """
                SELECT SalesOrderId, LineNumber, ProductId, Quantity, UnitPrice, LineTotal, Notes
                FROM sales.SalesOrderLine
                WHERE TenantId = @tenantId AND SalesOrderId IN @orderIds;
                """,
                new { tenantId = currentUser.TenantId, orderIds = orders.Select(o => o.Id).ToArray() },
                unitOfWork.Transaction,
                cancellationToken: ct))).ToLookup(l => l.SalesOrderId);

        var names = await catalog.GetProductNamesAsync(lines.SelectMany(g => g).Select(l => l.ProductId).Distinct().ToList(), ct);
        var versions = orders.Select(o => RowVersionToken.FromBinary(o.RowVer)).ToList();
        return new SyncEntityPage(
            orders.Select(o => (object)new
            {
                o.Id, o.DocumentNo, o.ClientDocumentId, o.Status, o.Channel, o.BranchId, o.Notes, o.GrandTotal, o.PaidAmount,
                o.OrderDate, TakenByUserId = o.SalesRepUserId,
                Lines = lines[o.Id].OrderBy(l => l.LineNumber).Select(l => new { l.ProductId, ProductName = names.GetValueOrDefault(l.ProductId, "Item"), l.Quantity, l.UnitPrice, l.LineTotal, l.Notes }),
            }).ToList(),
            RowVersionToken.Next(versions, request.Limit, request.UpperRowVersion),
            HasMore: orders.Count >= request.Limit);
    }

    private sealed record OrderRow(
        Guid Id, string? DocumentNo, Guid? ClientDocumentId, string Status, string Channel, Guid BranchId, string? Notes,
        decimal GrandTotal, decimal PaidAmount, DateTime OrderDate, Guid? SalesRepUserId, byte[] RowVer);

    private sealed record LineRow(
        Guid SalesOrderId, int LineNumber, Guid ProductId, decimal Quantity, decimal UnitPrice, decimal LineTotal, string? Notes);
}
