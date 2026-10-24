using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Finance.Domain;
using StockOrder.Modules.Finance.Infrastructure;
using StockOrder.Modules.Tenancy.Contracts;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Ids;
using StockOrder.SharedKernel.Numbers;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Finance.Application;

public static class FinancePermissions
{
    public const string PaymentReceive = "Finance.Payment.Receive";
    public const string CashSessionOperate = "Finance.CashSession.Operate";
    public const string CashSessionClose = "Finance.CashSession.Close";
}

public sealed record PaymentMethodItem(Guid Id, string Code, string Name, string Kind);

public sealed record GetPaymentMethodsQuery : IQuery<Result<IReadOnlyList<PaymentMethodItem>>>;

public sealed class GetPaymentMethodsQueryHandler(FinanceDbContext db, ICurrentUserContext currentUser)
    : IQueryHandler<GetPaymentMethodsQuery, Result<IReadOnlyList<PaymentMethodItem>>>
{
    public async ValueTask<Result<IReadOnlyList<PaymentMethodItem>>> Handle(GetPaymentMethodsQuery query, CancellationToken ct)
    {
        var methods = await db.PaymentMethods
            .Where(m => m.TenantId == currentUser.TenantId && m.IsActive)
            .OrderBy(m => m.Code == "CASH" ? 0 : 1).ThenBy(m => m.Name)
            .Select(m => new PaymentMethodItem(m.Id, m.Code, m.Name, m.Kind))
            .ToListAsync(ct);
        return Result.Success<IReadOnlyList<PaymentMethodItem>>(methods);
    }
}

/// <param name="CashTaken">Cash payments received in the session; ExpectedCash = OpeningFloat + CashTaken.</param>
public sealed record CashSessionSummary(
    Guid Id,
    Guid BranchId,
    DateOnly BusinessDate,
    string Status,
    DateTime OpenedAtUtc,
    decimal OpeningFloat,
    int PaymentCount,
    decimal CashTaken,
    decimal OtherTaken,
    decimal ExpectedCash,
    decimal? CountedCash,
    DateTime? ClosedAtUtc);

public sealed record CurrentCashSession(CashSessionSummary? Session);

/// <summary>The caller's own open shift at a branch, with what it has taken so far.</summary>
public sealed record GetCurrentCashSessionQuery(Guid BranchId) : IQuery<Result<CurrentCashSession>>;

public sealed class GetCurrentCashSessionQueryHandler(FinanceDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope)
    : IQueryHandler<GetCurrentCashSessionQuery, Result<CurrentCashSession>>
{
    public async ValueTask<Result<CurrentCashSession>> Handle(GetCurrentCashSessionQuery query, CancellationToken ct)
    {
        if (!BranchScope.For(await accessScope.GetGrantsAsync(ct), FinancePermissions.CashSessionOperate).Covers(query.BranchId))
        {
            return Result.Failure<CurrentCashSession>(Error.PermissionDenied("You don't take payments at this branch."));
        }

        var session = await db.CashSessions
            .Where(s => s.TenantId == currentUser.TenantId && s.BranchId == query.BranchId && s.OpenedBy == currentUser.UserId && s.Status == CashSession.Open)
            .OrderByDescending(s => s.OpenedAtUtc)
            .FirstOrDefaultAsync(ct);

        return Result.Success(new CurrentCashSession(session is null ? null : await CashSessionTotals.SummarizeAsync(db, session, ct)));
    }
}

internal static class CashSessionTotals
{
    public static async Task<CashSessionSummary> SummarizeAsync(FinanceDbContext db, CashSession session, CancellationToken ct)
    {
        var takings = await (
                from p in db.Payments
                join m in db.PaymentMethods on p.PaymentMethodId equals m.Id
                where p.TenantId == session.TenantId && p.CashSessionId == session.Id && p.Status == "Posted"
                select new { p.Direction, p.Amount, IsCash = m.Kind == "Cash" })
            .ToListAsync(ct);

        decimal Signed(decimal amount, string direction) => direction == "In" ? amount : -amount;
        var cash = takings.Where(t => t.IsCash).Sum(t => Signed(t.Amount, t.Direction));
        var other = takings.Where(t => !t.IsCash).Sum(t => Signed(t.Amount, t.Direction));

        return new CashSessionSummary(
            session.Id, session.BranchId, session.BusinessDate, session.Status, session.OpenedAtUtc, session.OpeningFloat,
            takings.Count, cash, other, Rounding.Money(session.OpeningFloat + cash), session.CountedCash, session.ClosedAtUtc);
    }
}

/// <summary>Starts the caller's shift at a branch; its business date is the tenant's today.</summary>
public sealed record OpenCashSessionCommand(Guid BranchId, decimal OpeningFloat) : ICommand<Result<CashSessionSummary>>;

public sealed class OpenCashSessionCommandValidator : AbstractValidator<OpenCashSessionCommand>
{
    public OpenCashSessionCommandValidator()
    {
        RuleFor(c => c.BranchId).NotEmpty();
        RuleFor(c => c.OpeningFloat).GreaterThanOrEqualTo(0).PrecisionScale(19, 2, ignoreTrailingZeros: true);
    }
}

public sealed class OpenCashSessionCommandHandler(
    FinanceDbContext db,
    ICurrentUserContext currentUser,
    IAccessScope accessScope,
    IBranchQueries branchQueries,
    ITenantQueries tenantQueries,
    IClock clock,
    IIdGenerator idGenerator)
    : ICommandHandler<OpenCashSessionCommand, Result<CashSessionSummary>>
{
    public async ValueTask<Result<CashSessionSummary>> Handle(OpenCashSessionCommand command, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        if (!BranchScope.For(await accessScope.GetGrantsAsync(ct), FinancePermissions.CashSessionOperate).Covers(command.BranchId))
        {
            return Result.Failure<CashSessionSummary>(Error.PermissionDenied("You don't take payments at this branch."));
        }

        if (await branchQueries.GetBranchAsync(tenantId, command.BranchId, ct) is not { IsActive: true })
        {
            return Result.Failure<CashSessionSummary>(Error.NotFound("Branch"));
        }

        var alreadyOpen = await db.CashSessions.AnyAsync(
            s => s.TenantId == tenantId && s.BranchId == command.BranchId && s.OpenedBy == currentUser.UserId && s.Status == CashSession.Open, ct);
        if (alreadyOpen)
        {
            return Result.Failure<CashSessionSummary>(Error.Conflict(ErrorCodes.InvalidStateTransition, "You already have an open cash session at this branch."));
        }

        var today = clock.TodayIn(await tenantQueries.GetTimeZoneIdAsync(tenantId, ct));
        var session = CashSession.Start(
            tenantId, idGenerator.NewId(), command.BranchId, today, currentUser.UserId, clock.UtcNow, Rounding.Money(command.OpeningFloat));
        db.CashSessions.Add(session);
        await db.SaveChangesAsync(ct);

        return Result.Success(await CashSessionTotals.SummarizeAsync(db, session, ct));
    }
}

/// <summary>Ends a shift: records the counted cash against what the session should hold.</summary>
public sealed record CloseCashSessionCommand(Guid SessionId, decimal CountedCash) : ICommand<Result<CashSessionSummary>>;

public sealed class CloseCashSessionCommandValidator : AbstractValidator<CloseCashSessionCommand>
{
    public CloseCashSessionCommandValidator()
    {
        RuleFor(c => c.SessionId).NotEmpty();
        RuleFor(c => c.CountedCash).GreaterThanOrEqualTo(0).PrecisionScale(19, 2, ignoreTrailingZeros: true);
    }
}

public sealed class CloseCashSessionCommandHandler(FinanceDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope, IClock clock)
    : ICommandHandler<CloseCashSessionCommand, Result<CashSessionSummary>>
{
    public async ValueTask<Result<CashSessionSummary>> Handle(CloseCashSessionCommand command, CancellationToken ct)
    {
        var session = await db.CashSessions.FirstOrDefaultAsync(s => s.TenantId == currentUser.TenantId && s.Id == command.SessionId, ct);
        var grants = await accessScope.GetGrantsAsync(ct);
        if (session is null || !BranchScope.For(grants, FinancePermissions.CashSessionOperate).Covers(session.BranchId))
        {
            return Result.Failure<CashSessionSummary>(Error.NotFound("Cash session"));
        }

        // Anyone with Close at the branch may close any of its sessions, e.g. a manager closing one left open overnight.
        if (!BranchScope.For(grants, FinancePermissions.CashSessionClose).Covers(session.BranchId))
        {
            return Result.Failure<CashSessionSummary>(Error.PermissionDenied("You can't close cash sessions at this branch."));
        }

        if (session.Status != CashSession.Open)
        {
            return Result.Failure<CashSessionSummary>(Error.Conflict(ErrorCodes.InvalidStateTransition, "This cash session is already closed."));
        }

        var totals = await CashSessionTotals.SummarizeAsync(db, session, ct);
        session.Close(currentUser.UserId, clock.UtcNow, totals.ExpectedCash, Rounding.Money(command.CountedCash));
        await db.SaveChangesAsync(ct);

        return Result.Success(await CashSessionTotals.SummarizeAsync(db, session, ct));
    }
}
