namespace StockOrder.SharedKernel.Time;

/// <summary>Injected everywhere instead of DateTime.UtcNow so tests are deterministic (section 5.8).</summary>
public interface IClock
{
    DateTime UtcNow { get; }

    /// <summary>Business "today" in the given IANA time zone (e.g. tenant.TimeZoneId), as a date-only value.</summary>
    DateOnly TodayIn(string timeZoneId);
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    public DateOnly TodayIn(string timeZoneId)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        return DateOnly.FromDateTime(local);
    }
}
