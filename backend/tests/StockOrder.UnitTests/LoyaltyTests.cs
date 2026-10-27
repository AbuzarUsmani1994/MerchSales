using Shouldly;
using StockOrder.Modules.Sales.Domain;
using Xunit;

namespace StockOrder.UnitTests;

/// <summary>Visit rewards: one member per phone however it's typed, five visits to a reward, visits lapse after six months.</summary>
public sealed class LoyaltyTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly LoyaltyRule Rule = LoyaltyRule.Defaults(Tenant);
    private static readonly DateOnly Day1 = new(2026, 1, 10);

    [Theory]
    [InlineData("03001234567")]
    [InlineData("0300-1234567")]
    [InlineData("0300 123 4567")]
    [InlineData("3001234567")]
    [InlineData("+92 300 1234567")]
    [InlineData("00923001234567")]
    [InlineData("923001234567")]
    public void Every_way_of_typing_a_pakistani_mobile_is_one_member(string typed)
    {
        PhoneNumbers.Normalize(typed).ShouldBe("923001234567");
        PhoneNumbers.Display("923001234567").ShouldBe("0300 1234567");
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("0300123456")]
    [InlineData("0300-123456789")]
    [InlineData("ali@example.com")]
    [InlineData("0300 12345x7")]
    public void Anything_else_is_refused(string typed) => PhoneNumbers.Normalize(typed).ShouldBeNull();

    [Fact]
    public void Foreign_numbers_need_a_plus() => PhoneNumbers.Normalize("+44 7700 900123").ShouldBe("447700900123");

    [Fact]
    public void Five_visits_make_a_reward_and_using_it_starts_over()
    {
        var log = Enumerable.Range(0, 5).Select(i => Visit(Day1.AddDays(i))).ToList();
        var ready = LoyaltyCalculator.Progress(log, Rule, Day1.AddDays(5));
        ready.Visits.ShouldBe(5);
        ready.RewardAvailable.ShouldBeTrue();

        log.Add(Reward(Day1.AddDays(6)));
        log.Add(Visit(Day1.AddDays(6)));
        var after = LoyaltyCalculator.Progress(log, Rule, Day1.AddDays(6));
        after.Visits.ShouldBe(1, "the bill that used the reward is visit 1 of the next round");
        after.RewardAvailable.ShouldBeFalse();
        after.CountedToday.ShouldBeTrue();
    }

    [Fact]
    public void Extra_visits_while_a_reward_waits_carry_over()
    {
        var log = Enumerable.Range(0, 7).Select(i => Visit(Day1.AddDays(i))).ToList();
        log.Add(Reward(Day1.AddDays(8)));
        LoyaltyCalculator.Progress(log, Rule, Day1.AddDays(8)).Visits.ShouldBe(2);
    }

    [Fact]
    public void Six_months_without_a_visit_start_the_count_again()
    {
        var log = new List<LoyaltyVisit> { Visit(Day1), Visit(Day1.AddDays(1)), Visit(Day1.AddDays(2)) };
        LoyaltyCalculator.Progress(log, Rule, Day1.AddDays(2).AddMonths(6)).Visits.ShouldBe(3, "exactly six months is still in time");
        LoyaltyCalculator.Progress(log, Rule, Day1.AddDays(3).AddMonths(6)).Visits.ShouldBe(0);

        log.Add(Visit(Day1.AddMonths(8)));
        LoyaltyCalculator.Progress(log, Rule, Day1.AddMonths(8)).Visits.ShouldBe(1, "a visit after the gap is visit 1");
    }

    [Fact]
    public void A_reward_is_spread_over_the_lines_and_the_total_drops_by_exactly_the_reward()
    {
        var order = SalesOrder.CreateDraft(Tenant, Guid.NewGuid(), SalesChannels.DineIn, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Day1, null, null, null, null, Guid.NewGuid(), DateTime.UtcNow);
        order.AddLine(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 1, 1850m, null, 0m, null);
        order.AddLine(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, 3, 233.33m, null, 0m, null);

        order.GrandTotal.ShouldBe(2549.99m);
        order.ApplyOrderDiscount(500m);

        order.DiscountTotal.ShouldBe(500m);
        order.GrandTotal.ShouldBe(2049.99m);
        order.Lines.Sum(l => l.LineTotal).ShouldBe(order.GrandTotal);
        order.Lines.ShouldAllBe(l => l.DiscountAmount > 0);
    }

    private static LoyaltyVisit Visit(DateOnly day) =>
        LoyaltyVisit.Create(Tenant, Guid.NewGuid(), Guid.Empty, LoyaltyVisitKind.Visit, Guid.NewGuid(), Guid.Empty, day, 2500m, null, Guid.Empty, DateTime.UtcNow);

    private static LoyaltyVisit Reward(DateOnly day) =>
        LoyaltyVisit.Create(Tenant, Guid.NewGuid(), Guid.Empty, LoyaltyVisitKind.RewardUsed, Guid.NewGuid(), Guid.Empty, day, 2500m, 500m, Guid.Empty, DateTime.UtcNow);
}
