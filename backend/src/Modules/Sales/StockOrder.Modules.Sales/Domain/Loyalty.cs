using StockOrder.SharedKernel.Entities;

namespace StockOrder.Modules.Sales.Domain;

/// <summary>
/// Phone numbers as members are found by: digits only, country code first, so "0300-1234567",
/// "03001234567" and "+92 300 1234567" are the same member (923001234567).
/// </summary>
public static class PhoneNumbers
{
    /// <summary>The stored form, or null when it isn't a phone number we can use.</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var international = input.TrimStart().StartsWith('+');
        var digits = new string(input.Where(char.IsAsciiDigit).ToArray());
        if (input.Any(c => !char.IsAsciiDigit(c) && c is not (' ' or '-' or '+' or '(' or ')')))
        {
            return null;
        }

        if (digits.StartsWith("00", StringComparison.Ordinal))
        {
            digits = digits[2..];
            international = true;
        }

        // Pakistan: 03xx xxxxxxx (11 digits) or 3xx xxxxxxx (10) become 92 3xx xxxxxxx.
        if (!international && digits.Length == 11 && digits.StartsWith('0'))
        {
            digits = "92" + digits[1..];
        }
        else if (!international && digits.Length == 10 && digits.StartsWith('3'))
        {
            digits = "92" + digits;
        }
        else if (!international && !(digits.Length == 12 && digits.StartsWith("92", StringComparison.Ordinal)))
        {
            return null;
        }

        return digits.Length is >= 8 and <= 15 ? digits : null;
    }

    /// <summary>How staff read it back: "0300 1234567" for Pakistani numbers, "+44…" for others.</summary>
    public static string Display(string normalized) =>
        normalized.Length == 12 && normalized.StartsWith("92", StringComparison.Ordinal)
            ? $"0{normalized[2..5]} {normalized[5..]}"
            : "+" + normalized;
}

public static class LoyaltyMemberStatus
{
    public const string Active = "Active";
    public const string Paused = "Paused";
}

/// <summary>sales.LoyaltyMember (015_Loyalty.sql): a customer whose visits count towards rewards.</summary>
public sealed class LoyaltyMember : TenantEntity
{
    public Guid CustomerId { get; private set; }
    public string Phone { get; private set; } = string.Empty;
    public string Status { get; private set; } = LoyaltyMemberStatus.Active;
    public DateTime JoinedAtUtc { get; private set; }
    public Guid? JoinedBy { get; private set; }
    public Guid? JoinedBranchId { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    private LoyaltyMember()
    {
    }

    public static LoyaltyMember Create(Guid tenantId, Guid id, Guid customerId, string phone, Guid? branchId, Guid userId, DateTime now) =>
        new()
        {
            TenantId = tenantId,
            Id = id,
            CustomerId = customerId,
            Phone = phone,
            JoinedAtUtc = now,
            JoinedBy = userId,
            JoinedBranchId = branchId,
        };

    public void SetStatus(string status, Guid userId, DateTime now)
    {
        Status = status;
        UpdatedAtUtc = now;
        UpdatedBy = userId;
    }
}

public static class LoyaltyVisitKind
{
    public const string Visit = "Visit";
    public const string RewardUsed = "RewardUsed";
}

/// <summary>sales.LoyaltyVisit: append-only (the database denies UPDATE/DELETE).</summary>
public sealed class LoyaltyVisit : TenantEntity
{
    public Guid MemberId { get; private set; }
    public string Kind { get; private set; } = LoyaltyVisitKind.Visit;
    public Guid SalesOrderId { get; private set; }
    public Guid BranchId { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public decimal BillAmount { get; private set; }
    public decimal? RewardAmount { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }

    private LoyaltyVisit()
    {
    }

    public static LoyaltyVisit Create(
        Guid tenantId, Guid id, Guid memberId, string kind, Guid orderId, Guid branchId, DateOnly businessDate,
        decimal billAmount, decimal? rewardAmount, Guid userId, DateTime now) =>
        new()
        {
            TenantId = tenantId,
            Id = id,
            MemberId = memberId,
            Kind = kind,
            SalesOrderId = orderId,
            BranchId = branchId,
            BusinessDate = businessDate,
            BillAmount = billAmount,
            RewardAmount = rewardAmount,
            CreatedAtUtc = now,
            CreatedBy = userId,
        };
}

/// <summary>sales.LoyaltyRule: the company's numbers. No row = the defaults agreed with the owner.</summary>
public sealed class LoyaltyRule
{
    public Guid TenantId { get; private set; }
    public int VisitsForReward { get; private set; }
    public decimal MinVisitBill { get; private set; }
    public decimal RewardAmount { get; private set; }
    public decimal MinRewardBill { get; private set; }
    public int ExpiryMonths { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public byte[] RowVer { get; private set; } = [];

    private LoyaltyRule()
    {
    }

    public static LoyaltyRule Defaults(Guid tenantId) => new()
    {
        TenantId = tenantId,
        VisitsForReward = 5,
        MinVisitBill = 2000m,
        RewardAmount = 500m,
        MinRewardBill = 2500m,
        ExpiryMonths = 6,
    };

    public void Set(int visitsForReward, decimal minVisitBill, decimal rewardAmount, decimal minRewardBill, int expiryMonths, Guid userId, DateTime now)
    {
        VisitsForReward = visitsForReward;
        MinVisitBill = minVisitBill;
        RewardAmount = rewardAmount;
        MinRewardBill = minRewardBill;
        ExpiryMonths = expiryMonths;
        UpdatedAtUtc = now;
        UpdatedBy = userId;
    }
}

/// <summary>sales.LoyaltyBranch: the programme switch (owner) and "cashiers can add members" (manager) per branch.</summary>
public sealed class LoyaltyBranch
{
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public bool IsEnabled { get; private set; }
    public bool CashierCanAddMembers { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public byte[] RowVer { get; private set; } = [];

    private LoyaltyBranch()
    {
    }

    public static LoyaltyBranch Create(Guid tenantId, Guid branchId) => new() { TenantId = tenantId, BranchId = branchId };

    public void Set(bool? isEnabled, bool? cashierCanAddMembers, Guid userId, DateTime now)
    {
        IsEnabled = isEnabled ?? IsEnabled;
        CashierCanAddMembers = cashierCanAddMembers ?? CashierCanAddMembers;
        UpdatedAtUtc = now;
        UpdatedBy = userId;
    }
}

/// <summary>Where a member stands: counted visits towards the next reward, and whether one is ready.</summary>
public sealed record LoyaltyProgress(int Visits, int VisitsForReward, bool RewardAvailable, bool CountedToday, DateOnly? LastVisit);

/// <summary>
/// Reads a member's visit log. Visits count up; a used reward takes <see cref="LoyaltyRule.VisitsForReward"/>
/// off (extra visits carry over); a gap longer than the expiry since the previous visit starts the
/// count again, and so does today being that far past the last visit.
/// </summary>
public static class LoyaltyCalculator
{
    public static LoyaltyProgress Progress(IEnumerable<LoyaltyVisit> log, LoyaltyRule rule, DateOnly today)
    {
        var count = 0;
        DateOnly? last = null;
        var countedToday = false;
        foreach (var entry in log)
        {
            if (entry.Kind == LoyaltyVisitKind.RewardUsed)
            {
                count = Math.Max(0, count - rule.VisitsForReward);
                continue;
            }

            if (entry.Kind != LoyaltyVisitKind.Visit)
            {
                continue;
            }

            if (last is { } previous && Expired(previous, entry.BusinessDate, rule))
            {
                count = 0;
            }

            count++;
            last = entry.BusinessDate;
            countedToday |= entry.BusinessDate == today;
        }

        if (last is { } latest && Expired(latest, today, rule))
        {
            count = 0;
        }

        return new LoyaltyProgress(count, rule.VisitsForReward, count >= rule.VisitsForReward, countedToday, last);
    }

    private static bool Expired(DateOnly lastVisit, DateOnly day, LoyaltyRule rule) => day > lastVisit.AddMonths(rule.ExpiryMonths);
}
