namespace StockOrder.SharedKernel.Errors;

/// <summary>
/// A typed, serializable failure. <see cref="Code"/> matches one of the ProblemDetails
/// codes in the Build Spec (section 9), e.g. "INSUFFICIENT_STOCK", "VALIDATION_FAILED".
/// </summary>
public sealed record Error(string Code, string Message, IReadOnlyDictionary<string, object?>? Details = null)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public static Error Validation(string message, IReadOnlyDictionary<string, object?>? details = null) =>
        new(ErrorCodes.ValidationFailed, message, details);

    public static Error NotFound(string entityName) =>
        new(ErrorCodes.NotFound, $"{entityName} was not found.");

    public static Error Conflict(string code, string message) => new(code, message);

    public static Error PermissionDenied(string message) => new(ErrorCodes.PermissionDenied, message);
}
