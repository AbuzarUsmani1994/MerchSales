namespace StockOrder.Modules.Platform.Infrastructure.Provisioning;

public interface ITenantProvisioningService
{
    Task<ProvisionTenantResult> ProvisionAsync(ProvisionTenantRequest request, CancellationToken ct);
}

public sealed record ProvisionTenantRequest(
    string Code,
    string Name,
    string Industry,
    string OwnerUsername,
    string OwnerDisplayName,
    string? OwnerEmail,
    string? OwnerPhone,
    string BranchCode,
    string BranchName,
    string WarehouseCode,
    string WarehouseName);

public sealed record ProvisionTenantResult(
    Guid TenantId,
    Guid BranchId,
    Guid WarehouseId,
    Guid OwnerUserId,
    string OwnerOneTimePassword,
    IReadOnlyDictionary<string, IReadOnlyList<string>> RolePermissionMap);
