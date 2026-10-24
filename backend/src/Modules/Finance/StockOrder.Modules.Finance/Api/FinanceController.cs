using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockOrder.Infrastructure.Api;
using StockOrder.Infrastructure.Idempotency;
using StockOrder.Modules.Finance.Application;

namespace StockOrder.Modules.Finance.Api;

/// <summary>Payment methods and cashier shifts (section 13 "Finance"). Branch checks happen in the handlers.</summary>
[ApiController]
[Route("api/v1/finance")]
public sealed class FinanceController(IMediator mediator) : ControllerBase
{
    [HttpGet("payment-methods")]
    [Authorize(Policy = FinancePermissions.PaymentReceive)]
    [BranchScoped]
    public async Task<IActionResult> PaymentMethods(CancellationToken ct) =>
        (await mediator.Send(new GetPaymentMethodsQuery(), ct)).ToActionResult();

    [HttpGet("cash-sessions/current")]
    [Authorize(Policy = FinancePermissions.CashSessionOperate)]
    [BranchScoped]
    public async Task<IActionResult> CurrentCashSession([FromQuery] Guid branchId, CancellationToken ct) =>
        (await mediator.Send(new GetCurrentCashSessionQuery(branchId), ct)).ToActionResult();

    [HttpPost("cash-sessions")]
    [Authorize(Policy = FinancePermissions.CashSessionOperate)]
    [BranchScoped]
    [Idempotent]
    public async Task<IActionResult> OpenCashSession(OpenCashSessionCommand command, CancellationToken ct) =>
        (await mediator.Send(command, ct)).ToActionResult();

    public sealed record CloseCashSessionRequest(decimal CountedCash);

    [HttpPost("cash-sessions/{sessionId:guid}/close")]
    [Authorize(Policy = FinancePermissions.CashSessionClose)]
    [BranchScoped]
    [Idempotent]
    public async Task<IActionResult> CloseCashSession(Guid sessionId, CloseCashSessionRequest request, CancellationToken ct) =>
        (await mediator.Send(new CloseCashSessionCommand(sessionId, request.CountedCash), ct)).ToActionResult();
}
