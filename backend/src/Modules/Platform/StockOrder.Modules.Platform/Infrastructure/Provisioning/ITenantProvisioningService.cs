namespace StockOrder.Modules.Platform.Infrastructure.Provisioning;

public interface ITenantProvisioningService
{
    Task<ProvisionTenantResult> ProvisionAsync(ProvisionTenantRequest request, CancellationToken ct);

    /// <summary>
    /// Adds whatever of the tenant's industry template is missing (catalog basics so far), for
    /// tenants provisioned before those sections were applied. Returns rows added; null if the
    /// tenant doesn't exist.
    /// </summary>
    Task<int?> ApplyMissingTemplateDataAsync(Guid tenantId, CancellationToken ct);
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
