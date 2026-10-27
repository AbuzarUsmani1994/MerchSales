using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Tenancy.Domain;
using StockOrder.Modules.Tenancy.Infrastructure;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Tenancy.Application.Branches;

/// <summary>A branch's customer menu link: the code a waiter's QR opens, or switched off (null).</summary>
public sealed record PublicMenuLinkView(Guid BranchId, bool IsOn, string? Code);

public sealed record GetPublicMenuLinkQuery(Guid BranchId) : IQuery<Result<PublicMenuLinkView>>;

/// <param name="On">True issues a new code (the old QR, if any, stops working); false switches the link off.</param>
public sealed record SetPublicMenuLinkCommand(Guid BranchId, bool On) : ICommand<Result<PublicMenuLinkView>>;

public sealed class GetPublicMenuLinkQueryHandler(TenancyDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope)
    : IQueryHandler<GetPublicMenuLinkQuery, Result<PublicMenuLinkView>>
{
    public async ValueTask<Result<PublicMenuLinkView>> Handle(GetPublicMenuLinkQuery query, CancellationToken ct)
    {
        if (await PublicMenuLinkRules.CheckAsync(db, currentUser, accessScope, query.BranchId, ct) is { } error)
        {
            return Result.Failure<PublicMenuLinkView>(error);
        }

        var key = PublicMenuSettings.KeyFor(query.BranchId);
        var code = await db.TenantSettings
            .Where(s => s.TenantId == currentUser.TenantId && s.SettingKey == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return Result.Success(PublicMenuLinkRules.View(query.BranchId, code));
    }
}

public sealed class SetPublicMenuLinkCommandHandler(
    TenancyDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope, IClock clock, IPublicMenuLinkLookup lookup)
    : ICommandHandler<SetPublicMenuLinkCommand, Result<PublicMenuLinkView>>
{
    public async ValueTask<Result<PublicMenuLinkView>> Handle(SetPublicMenuLinkCommand command, CancellationToken ct)
    {
        if (await PublicMenuLinkRules.CheckAsync(db, currentUser, accessScope, command.BranchId, ct) is { } error)
        {
            return Result.Failure<PublicMenuLinkView>(error);
        }

        var key = PublicMenuSettings.KeyFor(command.BranchId);
        var newValue = command.On ? PublicMenuCode.New() : string.Empty;
        var setting = await db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == currentUser.TenantId && s.SettingKey == key, ct);
        var oldCode = setting?.Value;

        // Switching off keeps the row with an empty value rather than deleting it: settings reach
        // phones by sync, and a deleted row would leave the old QR showing on them.
        if (setting is null)
        {
            db.TenantSettings.Add(TenantSetting.Create(currentUser.TenantId, key, newValue, clock.UtcNow, currentUser.UserId));
        }
        else
        {
            setting.SetValue(newValue, clock.UtcNow, currentUser.UserId);
        }

        await db.SaveChangesAsync(ct);
        if (!string.IsNullOrEmpty(oldCode))
        {
            await lookup.ForgetAsync(oldCode, ct);
        }

        return Result.Success(PublicMenuLinkRules.View(command.BranchId, newValue));
    }
}

internal static class PublicMenuLinkRules
{
    /// <summary>Restaurants only, and only for branches the caller's <c>Branches.Manage</c> role covers.</summary>
    public static async Task<Error?> CheckAsync(
        TenancyDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope, Guid branchId, CancellationToken ct)
    {
        var scope = await accessScope.GetBranchScopeAsync("Branches.Manage", ct);
        if (!scope.Covers(branchId) || !await db.Branches.AnyAsync(b => b.TenantId == currentUser.TenantId && b.Id == branchId, ct))
        {
            return Error.NotFound("Branch");
        }

        var industry = await db.Tenants.Where(t => t.Id == currentUser.TenantId).Select(t => t.Industry).FirstOrDefaultAsync(ct);
        return industry == TenantIndustry.Restaurant
            ? null
            : Error.Validation("The customer menu is only available for restaurants.");
    }

    public static PublicMenuLinkView View(Guid branchId, string? code) =>
        string.IsNullOrEmpty(code) ? new(branchId, false, null) : new(branchId, true, code);
}
