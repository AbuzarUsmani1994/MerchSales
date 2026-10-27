using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Sales.Domain;
using StockOrder.Modules.Sales.Infrastructure;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Ids;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Sales.Application;

/// <summary>What happened to the member's visits at this payment, for the cashier to tell the customer.</summary>
/// <param name="Message">Why the visit didn't count, when it didn't.</param>
public sealed record LoyaltyOutcome(
    string Phone, string? Name, bool VisitCounted, bool RewardUsed, decimal RewardAmount, int Visits, int VisitsForReward, bool RewardAvailable,
    string? Message);

/// <summary>
/// The visit-reward part of taking payment, inside the payment's transaction. <see cref="PrepareAsync"/>
/// runs before the invoice (the reward is a discount on it, and the order moves to the member's
/// customer record); <see cref="Record"/> runs once the payment succeeded and writes the log.
/// The member's row is locked first, so two tills paying for the same member at once can't both
/// count today's visit or both spend one reward.
/// </summary>
public sealed class LoyaltyCheckout(SalesDbContext db, ICurrentUserContext currentUser, LoyaltyStore store, IClock clock, IIdGenerator idGenerator)
{
    public sealed class Prepared
    {
        internal string Phone { get; init; } = string.Empty;
        internal LoyaltyMember? Member { get; init; }
        internal string? Name { get; init; }
        internal LoyaltyRule Rule { get; init; } = null!;
        internal List<LoyaltyVisit> Log { get; init; } = [];
        internal decimal BillBeforeReward { get; init; }
        internal bool RewardUsed { get; init; }
        internal string? Skip { get; init; }
    }

    public async Task<Result<Prepared?>> PrepareAsync(SalesOrder order, string? phoneInput, bool useReward, DateOnly businessDate, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phoneInput))
        {
            return useReward ? Fail(Error.Validation("Enter the member's phone number to use a reward.")) : Result.Success<Prepared?>(null);
        }

        if (PhoneNumbers.Normalize(phoneInput) is not { } phone)
        {
            return Fail(LoyaltyErrors.BadPhone);
        }

        var rule = await store.RulesAsync(ct);
        if (await store.BranchAsync(order.BranchId, ct) is not { IsEnabled: true })
        {
            return useReward
                ? Fail(Error.Validation("Visit rewards are switched off at this branch."))
                : Skip(phone, rule, "Visit rewards are switched off at this branch.");
        }

        var member = await store.MemberByPhoneAsync(phone, ct);
        if (member is null || member.Status != LoyaltyMemberStatus.Active)
        {
            var why = member is null ? "This number isn't a member." : "This membership is paused.";
            return useReward ? Fail(Error.Validation(why)) : Skip(phone, rule, why);
        }

        // Serialises payments for this member until the transaction ends.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT Id FROM sales.LoyaltyMember WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = {currentUser.TenantId} AND Id = {member.Id}", ct);
        var log = await store.LogAsync(member.Id, ct);
        var bill = order.GrandTotal;
        if (useReward)
        {
            var progress = LoyaltyCalculator.Progress(log, rule, businessDate);
            if (!progress.RewardAvailable)
            {
                return Fail(Error.Validation($"No reward yet: {Math.Min(progress.Visits, rule.VisitsForReward)} of {rule.VisitsForReward} visits."));
            }

            if (bill < rule.MinRewardBill)
            {
                return Fail(Error.Validation($"A reward needs a bill of Rs {rule.MinRewardBill:N0} or more."));
            }

            order.ApplyOrderDiscount(rule.RewardAmount);
        }

        order.AssignCustomer(member.CustomerId);
        var name = await db.Customers.Where(c => c.Id == member.CustomerId).Select(c => c.Name).FirstAsync(ct);
        return Result.Success<Prepared?>(new Prepared
        {
            Phone = phone, Member = member, Name = name, Rule = rule, Log = log, BillBeforeReward = bill, RewardUsed = useReward,
        });
    }

    /// <summary>Writes the reward used and the visit (if it counts); the caller's SaveChanges stores them.</summary>
    public LoyaltyOutcome Record(Prepared prepared, SalesOrder order, DateOnly businessDate)
    {
        var rule = prepared.Rule;
        if (prepared.Member is not { } member)
        {
            return new LoyaltyOutcome(PhoneNumbers.Display(prepared.Phone), null, false, false, 0, 0, rule.VisitsForReward, false, prepared.Skip);
        }

        var log = prepared.Log;
        var now = clock.UtcNow;
        if (prepared.RewardUsed)
        {
            log.Add(Add(member, LoyaltyVisitKind.RewardUsed, order, businessDate, prepared.BillBeforeReward, rule.RewardAmount, now));
        }

        var before = LoyaltyCalculator.Progress(log, rule, businessDate);
        string? message = null;
        if (prepared.BillBeforeReward < rule.MinVisitBill)
        {
            message = $"Not counted: visits count from Rs {rule.MinVisitBill:N0}.";
        }
        else if (before.CountedToday)
        {
            message = "Not counted: today's visit was already counted.";
        }
        else
        {
            log.Add(Add(member, LoyaltyVisitKind.Visit, order, businessDate, prepared.BillBeforeReward, null, now));
        }

        var after = LoyaltyCalculator.Progress(log, rule, businessDate);
        return new LoyaltyOutcome(
            PhoneNumbers.Display(prepared.Phone), prepared.Name, message is null, prepared.RewardUsed, prepared.RewardUsed ? rule.RewardAmount : 0,
            Math.Min(after.Visits, rule.VisitsForReward), rule.VisitsForReward, after.RewardAvailable, message);
    }

    private LoyaltyVisit Add(LoyaltyMember member, string kind, SalesOrder order, DateOnly businessDate, decimal bill, decimal? reward, DateTime now)
    {
        var entry = LoyaltyVisit.Create(currentUser.TenantId, idGenerator.NewId(), member.Id, kind, order.Id, order.BranchId, businessDate,
            bill, reward, currentUser.UserId, now);
        db.LoyaltyVisits.Add(entry);
        return entry;
    }

    private static Result<Prepared?> Fail(Error error) => Result.Failure<Prepared?>(error);

    private static Result<Prepared?> Skip(string phone, LoyaltyRule rule, string why) =>
        Result.Success<Prepared?>(new Prepared { Phone = phone, Rule = rule, Skip = why });
}
