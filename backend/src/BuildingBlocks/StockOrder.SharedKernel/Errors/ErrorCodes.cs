namespace StockOrder.SharedKernel.Errors;

/// <summary>
/// The error codes from the Build Spec's API conventions table (section 9). Every code
/// here maps to a fixed HTTP status in <see cref="ErrorCodeHttpStatus"/>.
/// </summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string DeviceRevoked = "DEVICE_REVOKED";
    public const string PermissionDenied = "PERMISSION_DENIED";
    public const string TenantSuspended = "TENANT_SUSPENDED";
    public const string NotFound = "NOT_FOUND";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string Duplicate = "DUPLICATE";
    public const string InvalidStateTransition = "INVALID_STATE_TRANSITION";
    public const string InsufficientStock = "INSUFFICIENT_STOCK";
    public const string BatchNotAvailable = "BATCH_NOT_AVAILABLE";
    public const string ShelfLifeTooShort = "SHELF_LIFE_TOO_SHORT";
    public const string CreditLimitExceeded = "CREDIT_LIMIT_EXCEEDED";
    public const string PriceNotFound = "PRICE_NOT_FOUND";
    public const string PeriodClosed = "PERIOD_CLOSED";
    public const string IdempotencyKeyReused = "IDEMPOTENCY_KEY_REUSED";
    public const string AppVersionUnsupported = "APP_VERSION_UNSUPPORTED";

    /// <summary>Codes not in the spec's table but needed by Phase 0 auth flows.</summary>
    public const string LockedOut = "LOCKED_OUT";
    public const string MfaRequired = "MFA_REQUIRED";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string RefreshTokenReused = "REFRESH_TOKEN_REUSED";
    public const string MustChangePassword = "MUST_CHANGE_PASSWORD";
}

public static class ErrorCodeHttpStatus
{
    public static int For(string code) => code switch
    {
        ErrorCodes.ValidationFailed => 400,
        ErrorCodes.Unauthenticated => 401,
        ErrorCodes.DeviceRevoked => 401,
        ErrorCodes.InvalidCredentials => 401,
        ErrorCodes.PermissionDenied => 403,
        ErrorCodes.TenantSuspended => 403,
        ErrorCodes.NotFound => 404,
        ErrorCodes.ConcurrencyConflict => 412,
        ErrorCodes.Duplicate => 409,
        ErrorCodes.InvalidStateTransition => 409,
        ErrorCodes.InsufficientStock => 409,
        ErrorCodes.BatchNotAvailable => 409,
        ErrorCodes.ShelfLifeTooShort => 409,
        ErrorCodes.CreditLimitExceeded => 409,
        ErrorCodes.PriceNotFound => 409,
        ErrorCodes.PeriodClosed => 409,
        ErrorCodes.LockedOut => 409,
        ErrorCodes.RefreshTokenReused => 409,
        ErrorCodes.MustChangePassword => 409,
        ErrorCodes.MfaRequired => 409,
        ErrorCodes.IdempotencyKeyReused => 422,
        ErrorCodes.AppVersionUnsupported => 426,
        _ => 500,
    };
}
