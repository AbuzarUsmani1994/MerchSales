using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockOrder.Infrastructure.Api;
using StockOrder.Infrastructure.Idempotency;
using StockOrder.Modules.Sales.Application;
using StockOrder.Modules.Sales.Domain;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Results;

namespace StockOrder.Modules.Sales.Api;

/// <summary>
/// Sales orders (section 8.6, 9). Staff take orders (also from the phone through sync); the
/// cashier queue lists confirmed ones and completes them with a payment. State actions need
/// If-Match with the order's ETag and an Idempotency-Key.
/// </summary>
[ApiController]
[Route("api/v1/sales/orders")]
public sealed class SalesOrdersController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = SalesPermissions.View)]
    [BranchScoped]
    public async Task<IActionResult> List(
        [FromQuery] Guid? branchId, [FromQuery] string? status, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var statuses = (status ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (await mediator.Send(new ListSalesOrdersQuery(branchId, statuses, search, page, pageSize), ct)).ToActionResult();
    }

    [HttpGet("{orderId:guid}")]
    [Authorize(Policy = SalesPermissions.View)]
    [BranchScoped]
    public async Task<IActionResult> Get(Guid orderId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetSalesOrderQuery(orderId), ct);
        if (result.IsSuccess)
        {
            Response.Headers.ETag = $"\"{result.Value.Etag}\"";
        }

        return result.ToActionResult();
    }

    /// <param name="TableId">A dine-in order at a table; the table stays taken until the order is paid or cancelled.</param>
    public sealed record CreateSalesOrderRequest(
        Guid BranchId, string? Channel, string? Notes, IReadOnlyList<CreateSalesOrderLine> Lines, string? CustomerPhone,
        Guid? TableId, int? Covers);

    [HttpPost]
    [Authorize(Policy = SalesPermissions.OrderCreate)]
    [BranchScoped]
    [Idempotent]
    public async Task<IActionResult> Create(CreateSalesOrderRequest request, CancellationToken ct) =>
        (await mediator.Send(new CreateSalesOrderCommand(
            request.BranchId, request.Channel ?? (request.TableId is null ? SalesChannels.BackOffice : SalesChannels.DineIn),
            request.Notes, request.Lines ?? [], CustomerPhone: request.CustomerPhone, TableId: request.TableId, Covers: request.Covers), ct)).ToActionResult();

    public sealed record AddRoundRequest(IReadOnlyList<CreateSalesOrderLine> Lines, string? Notes);

    /// <summary>The table orders more: the items join the open order and go to the kitchen as the next round.</summary>
    [HttpPost("{orderId:guid}/rounds")]
    [Authorize(Policy = SalesPermissions.OrderCreate)]
    [BranchScoped]
    [Idempotent]
    public async Task<IActionResult> AddRound(Guid orderId, AddRoundRequest request, CancellationToken ct)
    {
        if (RequireIfMatch() is { } missing)
        {
            return missing;
        }

        return (await mediator.Send(new AddOrderRoundCommand(orderId, Request.Headers.IfMatch.ToString(), request.Lines ?? [], request.Notes), ct))
            .ToActionResult();
    }

    /// <summary>The waiter tells the cashier the table wants its bill.</summary>
    [HttpPost("{orderId:guid}/request-bill")]
    [Authorize(Policy = SalesPermissions.OrderCreate)]
    [BranchScoped]
    [Idempotent]
    public async Task<IActionResult> RequestBill(Guid orderId, CancellationToken ct)
    {
        if (RequireIfMatch() is { } missing)
        {
            return missing;
        }

        return (await mediator.Send(new RequestBillCommand(orderId, Request.Headers.IfMatch.ToString()), ct)).ToActionResult();
    }

    /// <param name="Payments">A split bill: the parts must add up to the total (after any reward).</param>
    public sealed record CompleteSalesOrderRequest(
        Guid PaymentMethodId, decimal? AmountTendered, string? Reference, string? LoyaltyPhone, bool UseReward, IReadOnlyList<PaymentPart>? Payments);

    [HttpPost("{orderId:guid}/complete")]
    [Authorize(Policy = SalesPermissions.OrderFulfil)]
    [BranchScoped]
    [Idempotent]
    public async Task<IActionResult> Complete(Guid orderId, CompleteSalesOrderRequest request, CancellationToken ct)
    {
        if (RequireIfMatch() is { } missing)
        {
            return missing;
        }

        return (await mediator.Send(new CompleteSalesOrderCommand(
            orderId, Request.Headers.IfMatch.ToString(), request.PaymentMethodId, request.AmountTendered, request.Reference,
            request.LoyaltyPhone, request.UseReward, request.Payments is { Count: > 0 } parts ? parts : null), ct)).ToActionResult();
    }

    /// <summary>
    /// The paid order's 80 mm receipt as a PDF for the till's printer. <paramref name="tendered"/>
    /// prints cash received and change on the first print; a reprint passes <paramref name="copy"/>.
    /// </summary>
    [HttpGet("{orderId:guid}/receipt")]
    [Authorize(Policy = SalesPermissions.View)]
    [BranchScoped]
    public async Task<IActionResult> Receipt(Guid orderId, [FromQuery] decimal? tendered, [FromQuery] bool copy, CancellationToken ct)
    {
        var result = await mediator.Send(new GetReceiptQuery(orderId, tendered, copy), ct);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        Response.Headers.CacheControl = "no-store";
        return File(result.Value, "application/pdf");
    }

    public sealed record CancelSalesOrderRequest(string Reason);

    [HttpPost("{orderId:guid}/cancel")]
    [Authorize(Policy = SalesPermissions.OrderCancel)]
    [BranchScoped]
    [Idempotent]
    public async Task<IActionResult> Cancel(Guid orderId, CancelSalesOrderRequest request, CancellationToken ct)
    {
        if (RequireIfMatch() is { } missing)
        {
            return missing;
        }

        return (await mediator.Send(new CancelSalesOrderCommand(orderId, Request.Headers.IfMatch.ToString(), request.Reason ?? string.Empty), ct)).ToActionResult();
    }

    /// <summary>Section 9: state actions require If-Match; without it the action could overwrite a change the caller never saw.</summary>
    private IActionResult? RequireIfMatch() =>
        string.IsNullOrWhiteSpace(Request.Headers.IfMatch)
            ? Result.Failure(Error.Validation("The 'If-Match' header with the order's ETag is required.")).ToActionResult()
            : null;
}
