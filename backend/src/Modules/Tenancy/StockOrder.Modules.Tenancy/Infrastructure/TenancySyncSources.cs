using Dapper;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Sync.Contracts;
using StockOrder.SharedKernel.Context;

namespace StockOrder.Modules.Tenancy.Infrastructure;

/// <summary>Pulls tenancy.TenantSetting to the device (section 12 entity "settings").</summary>
internal sealed class SettingsSyncSource(IUnitOfWork unitOfWork, ICurrentUserContext currentUser) : ISyncSource
{
    public string Entity => "settings";

    public async Task<SyncEntityPage> PullAsync(SyncPullRequest request, CancellationToken ct)
    {
        var rows = (await unitOfWork.Connection.QueryAsync<SettingRow>(new CommandDefinition(
            """
            SELECT TOP (@limit) SettingKey AS [Key], Value, RowVer
            FROM tenancy.TenantSetting
            WHERE TenantId = @tenantId AND RowVer > @after AND RowVer < @upper
            ORDER BY RowVer;
            """,
            TenancySyncSql.Parameters(currentUser.TenantId, request),
            unitOfWork.Transaction,
            cancellationToken: ct))).ToList();

        return TenancySyncSql.Page(rows, r => r.RowVer, r => new { r.Key, r.Value }, request);
    }

    private sealed record SettingRow(string Key, string Value, byte[] RowVer);
}

/// <summary>Pulls tenancy.TenantFeature (section 12 entity "features"): menus on the phone follow these.</summary>
internal sealed class FeaturesSyncSource(IUnitOfWork unitOfWork, ICurrentUserContext currentUser) : ISyncSource
{
    public string Entity => "features";

    public async Task<SyncEntityPage> PullAsync(SyncPullRequest request, CancellationToken ct)
    {
        var rows = (await unitOfWork.Connection.QueryAsync<FeatureRow>(new CommandDefinition(
            """
            SELECT TOP (@limit) FeatureKey AS [Key], IsEnabled, RowVer
            FROM tenancy.TenantFeature
            WHERE TenantId = @tenantId AND RowVer > @after AND RowVer < @upper
            ORDER BY RowVer;
            """,
            TenancySyncSql.Parameters(currentUser.TenantId, request),
            unitOfWork.Transaction,
            cancellationToken: ct))).ToList();

        return TenancySyncSql.Page(rows, r => r.RowVer, r => new { r.Key, r.IsEnabled }, request);
    }

    private sealed record FeatureRow(string Key, bool IsEnabled, byte[] RowVer);
}

/// <summary>
/// Section 12 entity "branches": the branches the user works at (any role there), all of them
/// for an all-branches role. The phone uses it to name and pick the branch it takes orders for.
/// </summary>
internal sealed class BranchesSyncSource(IUnitOfWork unitOfWork, ICurrentUserContext currentUser, IAccessScope accessScope) : ISyncSource
{
    public string Entity => "branches";

    public async Task<SyncEntityPage> PullAsync(SyncPullRequest request, CancellationToken ct)
    {
        var grants = await accessScope.GetGrantsAsync(ct);
        var parameters = TenancySyncSql.Parameters(currentUser.TenantId, request);
        var branchIds = grants.Where(g => g.BranchId is not null).Select(g => g.BranchId!.Value).Distinct().ToArray();
        parameters.Add("allBranches", grants.Any(g => g.BranchId is null));
        parameters.Add("branchIds", branchIds.Length > 0 ? branchIds : [Guid.Empty]);

        var rows = (await unitOfWork.Connection.QueryAsync<BranchRow>(new CommandDefinition(
            """
            SELECT TOP (@limit) Id, Code, Name, IsActive, RowVer
            FROM tenancy.Branch
            WHERE TenantId = @tenantId AND RowVer > @after AND RowVer < @upper
              AND (@allBranches = 1 OR Id IN @branchIds)
            ORDER BY RowVer;
            """,
            parameters,
            unitOfWork.Transaction,
            cancellationToken: ct))).ToList();

        return TenancySyncSql.Page(rows, r => r.RowVer, r => new { r.Id, r.Code, r.Name, r.IsActive }, request);
    }

    private sealed record BranchRow(Guid Id, string Code, string Name, bool IsActive, byte[] RowVer);
}

internal static class TenancySyncSql
{
    public static DynamicParameters Parameters(Guid tenantId, SyncPullRequest request)
    {
        var parameters = new DynamicParameters();
        parameters.Add("tenantId", tenantId);
        parameters.Add("limit", request.Limit);
        parameters.Add("after", RowVersionToken.ToBinary(RowVersionToken.Parse(request.Token)), System.Data.DbType.Binary, size: 8);
        parameters.Add("upper", RowVersionToken.ToBinary(request.UpperRowVersion), System.Data.DbType.Binary, size: 8);
        return parameters;
    }

    public static SyncEntityPage Page<TRow>(
        List<TRow> rows, Func<TRow, byte[]> rowVersion, Func<TRow, object> project, SyncPullRequest request)
    {
        var versions = rows.Select(r => RowVersionToken.FromBinary(rowVersion(r))).ToList();
        return new SyncEntityPage(
            rows.Select(project).ToList(),
            RowVersionToken.Next(versions, request.Limit, request.UpperRowVersion),
            HasMore: rows.Count >= request.Limit);
    }
}
