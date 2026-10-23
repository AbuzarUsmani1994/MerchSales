namespace StockOrder.Modules.Platform.Infrastructure.Bootstrap;

public interface IBootstrapPlatformService
{
    Task<BootstrapResult> BootstrapAsync(string adminUsername, CancellationToken ct);
}

public sealed record BootstrapResult(Guid TenantId, Guid AdminUserId, string OneTimePassword);
