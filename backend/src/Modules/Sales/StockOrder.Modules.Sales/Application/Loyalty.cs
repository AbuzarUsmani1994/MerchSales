using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Identity.Contracts;
using StockOrder.Modules.Sales.Domain;
using StockOrder.Modules.Sales.Infrastructure;
using StockOrder.Modules.Tenancy.Contracts;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Ids;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Sales.Application;

/// <summary>
/// Visit rewards (015_Loyalty.sql, docs/decisions.md "Visit rewards"): members by phone number,
/// one counted visit per paid bill above a minimum (once a day), a fixed amount off after enough
/// visits. Off until the owner switches it on per branch; owners and managers add members, and
/// cashiers can at a branch where a manager allows it.
/// </summary>
public static class LoyaltyPermissions
{
    public const string Manage = "Sales.Loyalty.Manage";

    /// <summary>The owner's switch and the company's numbers: Settings.Manage from an all-branches role.</summary>
    public const string Settings = "Settings.Manage";

    public static bool IsOwner(IEnumerable<PermissionGrant> grants) => grants.Any(g => g.Code == Settings && g.BranchId is null);
}

public sealed record LoyaltyRulesView(int VisitsForReward, decimal MinVisitBill, decimal RewardAmount, decimal MinRewardBill, int ExpiryMonths);

public sealed record LoyaltyBranchView(Guid BranchId, string BranchName, bool IsEnabled, bool CashierCanAddMembers);

/// <param name="CanEditRules">The owner: the numbers and every branch's programme switch.</param>
public sealed record LoyaltySettingsView(LoyaltyRulesView Rules, bool CanEditRules, IReadOnlyList<LoyaltyBranchView> Branches);

/// <summary>What the cashier sees for a phone number at a branch.</summary>
public sealed record LoyaltyLookupView(
    string Phone,
    bool ProgrammeOn,
    bool IsMember,
    Guid? MemberId,
    string? Name,
    string? Status,
    int Visits,
    int VisitsForReward,
    bool RewardAvailable,
    bool CountedToday,
    decimal RewardAmount,
    decimal MinRewardBill,
    decimal MinVisitBill,
    bool CanAddMember);

public sealed record LoyaltyMemberRow(
    Guid Id,
    string Phone,
    string Name,
    string Status,
    int Visits,
    int VisitsForReward,
    bool RewardAvailable,
    int RewardsUsed,
    DateOnly? LastVisit,
    DateTime JoinedAtUtc,
    string? JoinedBy,
    string? JoinedBranch);

/// <summary>Reads and checks the shared pieces: rules, branch switches, a member's log.</summary>
public sealed class LoyaltyStore(SalesDbContext db, ICurrentUserContext currentUser)
{
    public async Task<LoyaltyRule> RulesAsync(CancellationToken ct) =>
        await db.LoyaltyRules.FirstOrDefaultAsync(r => r.TenantId == currentUser.TenantId, ct) ?? LoyaltyRule.Defaults(currentUser.TenantId);

    public async Task<LoyaltyBranch?> BranchAsync(Guid branchId, CancellationToken ct) =>
        await db.LoyaltyBranches.FirstOrDefaultAsync(b => b.TenantId == currentUser.TenantId && b.BranchId == branchId, ct);

    public Task<LoyaltyMember?> MemberByPhoneAsync(string phone, CancellationToken ct) =>
        db.LoyaltyMembers.FirstOrDefaultAsync(m => m.TenantId == currentUser.TenantId && m.Phone == phone, ct);

    public async Task<List<LoyaltyVisit>> LogAsync(Guid memberId, CancellationToken ct) =>
        await db.LoyaltyVisits.Where(v => v.TenantId == currentUser.TenantId && v.MemberId == memberId)
            .OrderBy(v => v.ClusterKey).ToListAsync(ct);

    /// <summary>Who may add a member at this branch: owners and managers always, cashiers when both switches are on.</summary>
    /// <remarks>Without a branch (the Members screen) any owner or manager may: members are company-wide.</remarks>
    public static bool CanAddMember(IReadOnlyList<PermissionGrant> grants, Guid? branchId, LoyaltyBranch? branch) =>
        (branchId is null ? !BranchScope.For(grants, LoyaltyPermissions.Manage).IsEmpty : BranchScope.For(grants, LoyaltyPermissions.Manage).Covers(branchId))
        || (branchId is { } id && branch is { IsEnabled: true, CashierCanAddMembers: true }
            && BranchScope.For(grants, SalesPermissions.OrderFulfil).Covers(id));

    public static LoyaltyRulesView View(LoyaltyRule rule) =>
        new(rule.VisitsForReward, rule.MinVisitBill, rule.RewardAmount, rule.MinRewardBill, rule.ExpiryMonths);
}

// ---------------------------------------------------------------- settings and switches

public sealed record GetLoyaltySettingsQuery : IQuery<Result<LoyaltySettingsView>>;

public sealed class GetLoyaltySettingsQueryHandler(
    SalesDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope, IBranchQueries branches, LoyaltyStore store)
    : IQueryHandler<GetLoyaltySettingsQuery, Result<LoyaltySettingsView>>
{
    public async ValueTask<Result<LoyaltySettingsView>> Handle(GetLoyaltySettingsQuery query, CancellationToken ct)
    {
        var grants = await accessScope.GetGrantsAsync(ct);
        var owner = LoyaltyPermissions.IsOwner(grants);
        var scope = BranchScope.For(grants, LoyaltyPermissions.Manage);
        var switches = await db.LoyaltyBranches.Where(b => b.TenantId == currentUser.TenantId).ToDictionaryAsync(b => b.BranchId, ct);
        var rows = (await branches.GetBranchesAsync(currentUser.TenantId, ct))
            .Where(b => b.IsActive && (owner || scope.Covers(b.Id)))
            .Select(b => switches.GetValueOrDefault(b.Id) is { } s
                ? new LoyaltyBranchView(b.Id, b.Name, s.IsEnabled, s.CashierCanAddMembers)
                : new LoyaltyBranchView(b.Id, b.Name, false, false))
            .ToList();

        return Result.Success(new LoyaltySettingsView(LoyaltyStore.View(await store.RulesAsync(ct)), owner, rows));
    }
}

public sealed record SaveLoyaltyRulesCommand(int VisitsForReward, decimal MinVisitBill, decimal RewardAmount, decimal MinRewardBill, int ExpiryMonths)
    : ICommand<Result<LoyaltyRulesView>>;

public sealed class SaveLoyaltyRulesCommandValidator : AbstractValidator<SaveLoyaltyRulesCommand>
{
    public SaveLoyaltyRulesCommandValidator()
    {
        RuleFor(c => c.VisitsForReward).InclusiveBetween(1, 100);
        RuleFor(c => c.MinVisitBill).GreaterThanOrEqualTo(0).PrecisionScale(19, 2, ignoreTrailingZeros: true);
        RuleFor(c => c.RewardAmount).GreaterThan(0).PrecisionScale(19, 2, ignoreTrailingZeros: true);
        RuleFor(c => c.MinRewardBill).GreaterThanOrEqualTo(c => c.RewardAmount)
            .WithMessage("The bill that uses a reward must be at least the reward itself.");
        RuleFor(c => c.ExpiryMonths).InclusiveBetween(1, 60);
    }
}

public sealed class SaveLoyaltyRulesCommandHandler(SalesDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope, IClock clock)
    : ICommandHandler<SaveLoyaltyRulesCommand, Result<LoyaltyRulesView>>
{
    public async ValueTask<Result<LoyaltyRulesView>> Handle(SaveLoyaltyRulesCommand command, CancellationToken ct)
    {
        if (!LoyaltyPermissions.IsOwner(await accessScope.GetGrantsAsync(ct)))
        {
            return Result.Failure<LoyaltyRulesView>(Error.PermissionDenied("Only the owner can change the visit reward rules."));
        }

        var rule = await db.LoyaltyRules.FirstOrDefaultAsync(r => r.TenantId == currentUser.TenantId, ct);
        if (rule is null)
        {
            rule = LoyaltyRule.Defaults(currentUser.TenantId);
            db.LoyaltyRules.Add(rule);
        }

        rule.Set(command.VisitsForReward, command.MinVisitBill, command.RewardAmount, command.MinRewardBill, command.ExpiryMonths,
            currentUser.UserId, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Result.Success(LoyaltyStore.View(rule));
    }
}

/// <param name="IsEnabled">The programme at this branch: the owner's switch.</param>
/// <param name="CashierCanAddMembers">Cashiers may add members here: the owner or this branch's manager.</param>
public sealed record SetLoyaltyBranchCommand(Guid BranchId, bool? IsEnabled, bool? CashierCanAddMembers) : ICommand<Result<LoyaltyBranchView>>;

public sealed class SetLoyaltyBranchCommandHandler(
    SalesDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope, IBranchQueries branches, IClock clock)
    : ICommandHandler<SetLoyaltyBranchCommand, Result<LoyaltyBranchView>>
{
    public async ValueTask<Result<LoyaltyBranchView>> Handle(SetLoyaltyBranchCommand command, CancellationToken ct)
    {
        var grants = await accessScope.GetGrantsAsync(ct);
        if (await branches.GetBranchAsync(currentUser.TenantId, command.BranchId, ct) is not { } branch
            || !(LoyaltyPermissions.IsOwner(grants) || BranchScope.For(grants, LoyaltyPermissions.Manage).Covers(command.BranchId)))
        {
            return Result.Failure<LoyaltyBranchView>(Error.NotFound("Branch"));
        }

        if (command.IsEnabled is not null && !LoyaltyPermissions.IsOwner(grants))
        {
            return Result.Failure<LoyaltyBranchView>(Error.PermissionDenied("Only the owner can switch visit rewards on or off."));
        }

        if (command.CashierCanAddMembers is not null && !BranchScope.For(grants, LoyaltyPermissions.Manage).Covers(command.BranchId))
        {
            return Result.Failure<LoyaltyBranchView>(Error.PermissionDenied("Only the owner or this branch's manager can change this."));
        }

        var row = await db.LoyaltyBranches.FirstOrDefaultAsync(b => b.TenantId == currentUser.TenantId && b.BranchId == command.BranchId, ct);
        if (row is null)
        {
            row = LoyaltyBranch.Create(currentUser.TenantId, command.BranchId);
            db.LoyaltyBranches.Add(row);
        }

        row.Set(command.IsEnabled, command.CashierCanAddMembers, currentUser.UserId, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Result.Success(new LoyaltyBranchView(branch.Id, branch.Name, row.IsEnabled, row.CashierCanAddMembers));
    }
}

// ---------------------------------------------------------------- the cashier: look up, add

/// <summary>Whether the payment screen shows visit rewards at this branch, before any phone is typed.</summary>
public sealed record LoyaltyBranchStatus(bool ProgrammeOn, bool CanAddMember, LoyaltyRulesView Rules);

public sealed record GetLoyaltyBranchStatusQuery(Guid BranchId) : IQuery<Result<LoyaltyBranchStatus>>;

public sealed class GetLoyaltyBranchStatusQueryHandler(IAccessScope accessScope, LoyaltyStore store)
    : IQueryHandler<GetLoyaltyBranchStatusQuery, Result<LoyaltyBranchStatus>>
{
    public async ValueTask<Result<LoyaltyBranchStatus>> Handle(GetLoyaltyBranchStatusQuery query, CancellationToken ct)
    {
        var grants = await accessScope.GetGrantsAsync(ct);
        var branch = await store.BranchAsync(query.BranchId, ct);
        return Result.Success(new LoyaltyBranchStatus(
            branch?.IsEnabled ?? false, LoyaltyStore.CanAddMember(grants, query.BranchId, branch), LoyaltyStore.View(await store.RulesAsync(ct))));
    }
}

public sealed record LookupLoyaltyMemberQuery(string Phone, Guid BranchId) : IQuery<Result<LoyaltyLookupView>>;

public sealed class LookupLoyaltyMemberQueryHandler(
    SalesDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope, ITenantQueries tenantQueries, LoyaltyStore store, IClock clock)
    : IQueryHandler<LookupLoyaltyMemberQuery, Result<LoyaltyLookupView>>
{
    public async ValueTask<Result<LoyaltyLookupView>> Handle(LookupLoyaltyMemberQuery query, CancellationToken ct)
    {
        var grants = await accessScope.GetGrantsAsync(ct);
        if (!BranchScope.For(grants, SalesPermissions.OrderFulfil).Covers(query.BranchId)
            && !BranchScope.For(grants, LoyaltyPermissions.Manage).Covers(query.BranchId))
        {
            return Result.Failure<LoyaltyLookupView>(Error.PermissionDenied("You don't take payments at this branch."));
        }

        if (PhoneNumbers.Normalize(query.Phone) is not { } phone)
        {
            return Result.Failure<LoyaltyLookupView>(LoyaltyErrors.BadPhone);
        }

        var today = clock.TodayIn(await tenantQueries.GetTimeZoneIdAsync(currentUser.TenantId, ct));
        return Result.Success(await LoyaltyViews.LookupAsync(db, store, grants, phone, query.BranchId, today, ct));
    }
}

/// <param name="BranchId">Where they joined: the till's branch, or null from the Members screen (owners and managers).</param>
public sealed record AddLoyaltyMemberCommand(string Phone, string? Name, Guid? BranchId) : ICommand<Result<LoyaltyLookupView>>;

public sealed class AddLoyaltyMemberCommandValidator : AbstractValidator<AddLoyaltyMemberCommand>
{
    public AddLoyaltyMemberCommandValidator()
    {
        RuleFor(c => c.Phone).NotEmpty().MaximumLength(30);
        RuleFor(c => c.Name).MaximumLength(200);
    }
}

public sealed class AddLoyaltyMemberCommandHandler(
    SalesDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope, ITenantQueries tenantQueries,
    LoyaltyStore store, IClock clock, IIdGenerator idGenerator)
    : ICommandHandler<AddLoyaltyMemberCommand, Result<LoyaltyLookupView>>
{
    public async ValueTask<Result<LoyaltyLookupView>> Handle(AddLoyaltyMemberCommand command, CancellationToken ct)
    {
        if (PhoneNumbers.Normalize(command.Phone) is not { } phone)
        {
            return Result.Failure<LoyaltyLookupView>(LoyaltyErrors.BadPhone);
        }

        var grants = await accessScope.GetGrantsAsync(ct);
        var branch = command.BranchId is { } branchId ? await store.BranchAsync(branchId, ct) : null;
        if (!LoyaltyStore.CanAddMember(grants, command.BranchId, branch))
        {
            return Result.Failure<LoyaltyLookupView>(Error.PermissionDenied(
                "Adding members here needs a manager, or a manager to switch on \"Cashiers can add members\"."));
        }

        if (await store.MemberByPhoneAsync(phone, ct) is not null)
        {
            return Result.Failure<LoyaltyLookupView>(Error.Conflict(ErrorCodes.Duplicate, $"{PhoneNumbers.Display(phone)} is already a member."));
        }

        var customer = Customer.ForMember(currentUser.TenantId, idGenerator.NewId(), phone, command.Name);
        db.Customers.Add(customer);
        db.LoyaltyMembers.Add(LoyaltyMember.Create(
            currentUser.TenantId, idGenerator.NewId(), customer.Id, phone, command.BranchId, currentUser.UserId, clock.UtcNow));
        await db.SaveChangesAsync(ct);

        var today = clock.TodayIn(await tenantQueries.GetTimeZoneIdAsync(currentUser.TenantId, ct));
        return Result.Success(await LoyaltyViews.LookupAsync(db, store, grants, phone, command.BranchId, today, ct));
    }
}

// ---------------------------------------------------------------- the Members screen

public sealed record ListLoyaltyMembersQuery(string? Search, int Page, int PageSize) : IQuery<Result<PagedList<LoyaltyMemberRow>>>;

public sealed class ListLoyaltyMembersQueryHandler(
    SalesDbContext db, ICurrentUserContext currentUser, ITenantQueries tenantQueries, IUserDirectory users, IBranchQueries branches,
    LoyaltyStore store, IClock clock)
    : IQueryHandler<ListLoyaltyMembersQuery, Result<PagedList<LoyaltyMemberRow>>>
{
    public async ValueTask<Result<PagedList<LoyaltyMemberRow>>> Handle(ListLoyaltyMembersQuery query, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var members =
            from m in db.LoyaltyMembers
            join c in db.Customers on m.CustomerId equals c.Id
            where m.TenantId == tenantId
            select new { Member = m, c.Name };
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            var digits = PhoneNumbers.Normalize(term) ?? new string(term.Where(char.IsAsciiDigit).ToArray());
            members = members.Where(x => x.Name.Contains(term) || (digits.Length >= 3 && x.Member.Phone.Contains(digits)));
        }

        var total = await members.CountAsync(ct);
        var rows = await members.OrderByDescending(x => x.Member.JoinedAtUtc).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var ids = rows.Select(r => r.Member.Id).ToList();
        var logs = (await db.LoyaltyVisits.Where(v => v.TenantId == tenantId && ids.Contains(v.MemberId)).OrderBy(v => v.ClusterKey).ToListAsync(ct))
            .ToLookup(v => v.MemberId);
        var rule = await store.RulesAsync(ct);
        var today = clock.TodayIn(await tenantQueries.GetTimeZoneIdAsync(tenantId, ct));
        var joinedBy = await users.GetDisplayNamesAsync(rows.Where(r => r.Member.JoinedBy is not null).Select(r => r.Member.JoinedBy!.Value).Distinct().ToList(), ct);
        var branchNames = (await branches.GetBranchesAsync(tenantId, ct)).ToDictionary(b => b.Id, b => b.Name);

        var items = rows.Select(r =>
        {
            var log = logs[r.Member.Id].ToList();
            var progress = LoyaltyCalculator.Progress(log, rule, today);
            return new LoyaltyMemberRow(
                r.Member.Id, PhoneNumbers.Display(r.Member.Phone), r.Name, r.Member.Status,
                Math.Min(progress.Visits, rule.VisitsForReward), rule.VisitsForReward, progress.RewardAvailable,
                log.Count(v => v.Kind == LoyaltyVisitKind.RewardUsed), progress.LastVisit, r.Member.JoinedAtUtc,
                r.Member.JoinedBy is { } by ? joinedBy.GetValueOrDefault(by) : null,
                r.Member.JoinedBranchId is { } b ? branchNames.GetValueOrDefault(b) : null);
        }).ToList();

        return Result.Success(new PagedList<LoyaltyMemberRow>(items, page, pageSize, total));
    }
}

public sealed record UpdateLoyaltyMemberCommand(Guid MemberId, string? Name, string? Status) : ICommand<Result>;

public sealed class UpdateLoyaltyMemberCommandValidator : AbstractValidator<UpdateLoyaltyMemberCommand>
{
    public UpdateLoyaltyMemberCommandValidator()
    {
        RuleFor(c => c.Name).MaximumLength(200);
        RuleFor(c => c.Status).Must(s => s is null or LoyaltyMemberStatus.Active or LoyaltyMemberStatus.Paused)
            .WithMessage("Status must be Active or Paused.");
    }
}

public sealed class UpdateLoyaltyMemberCommandHandler(SalesDbContext db, ICurrentUserContext currentUser, IClock clock)
    : ICommandHandler<UpdateLoyaltyMemberCommand, Result>
{
    public async ValueTask<Result> Handle(UpdateLoyaltyMemberCommand command, CancellationToken ct)
    {
        var member = await db.LoyaltyMembers.FirstOrDefaultAsync(m => m.TenantId == currentUser.TenantId && m.Id == command.MemberId, ct);
        if (member is null)
        {
            return Result.Failure(Error.NotFound("Member"));
        }

        if (command.Status is { } status && status != member.Status)
        {
            member.SetStatus(status, currentUser.UserId, clock.UtcNow);
        }

        if (!string.IsNullOrWhiteSpace(command.Name))
        {
            var customer = await db.Customers.FirstAsync(c => c.TenantId == currentUser.TenantId && c.Id == member.CustomerId, ct);
            customer.Rename(command.Name);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal static class LoyaltyErrors
{
    public static readonly Error BadPhone = Error.Validation("Enter a mobile number like 0300 1234567.");
}

internal static class LoyaltyViews
{
    public static async Task<LoyaltyLookupView> LookupAsync(
        SalesDbContext db, LoyaltyStore store, IReadOnlyList<PermissionGrant> grants, string phone, Guid? branchId, DateOnly today, CancellationToken ct)
    {
        var rule = await store.RulesAsync(ct);
        var branch = branchId is { } id ? await store.BranchAsync(id, ct) : null;
        var programmeOn = branch?.IsEnabled ?? false;
        var member = await store.MemberByPhoneAsync(phone, ct);
        if (member is null)
        {
            return new LoyaltyLookupView(
                PhoneNumbers.Display(phone), programmeOn, false, null, null, null, 0, rule.VisitsForReward, false, false,
                rule.RewardAmount, rule.MinRewardBill, rule.MinVisitBill, LoyaltyStore.CanAddMember(grants, branchId, branch));
        }

        var name = await db.Customers.Where(c => c.Id == member.CustomerId).Select(c => c.Name).FirstAsync(ct);
        var progress = LoyaltyCalculator.Progress(await store.LogAsync(member.Id, ct), rule, today);
        return new LoyaltyLookupView(
            PhoneNumbers.Display(phone), programmeOn, true, member.Id, name, member.Status,
            Math.Min(progress.Visits, rule.VisitsForReward), rule.VisitsForReward, progress.RewardAvailable, progress.CountedToday,
            rule.RewardAmount, rule.MinRewardBill, rule.MinVisitBill, false);
    }
}
