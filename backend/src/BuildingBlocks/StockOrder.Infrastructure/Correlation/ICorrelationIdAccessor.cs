namespace StockOrder.Infrastructure.Correlation;

/// <summary>Flows the current request's correlation id (section 9) into logs, audit rows and ProblemDetails.</summary>
public interface ICorrelationIdAccessor
{
    string? CorrelationId { get; }
    void Set(string correlationId);
}

/// <summary>AsyncLocal-backed so it also works in the Worker's per-tenant background jobs, not just HTTP requests.</summary>
public sealed class CorrelationIdAccessor : ICorrelationIdAccessor
{
    private static readonly AsyncLocal<string?> Current = new();

    public string? CorrelationId => Current.Value;

    public void Set(string correlationId) => Current.Value = correlationId;
}
