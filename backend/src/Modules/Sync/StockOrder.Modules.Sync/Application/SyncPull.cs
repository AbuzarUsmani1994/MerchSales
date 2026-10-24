using System.Text;
using System.Text.Json;
using Dapper;
using Mediator;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Sync.Contracts;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Sync.Application;

public sealed record SyncTombstone(string Entity, Guid Id);

public sealed record SyncPullResult(
    IReadOnlyDictionary<string, string> Tokens,
    IReadOnlyDictionary<string, bool> HasMore,
    IReadOnlyDictionary<string, IReadOnlyList<object>> Changes,
    IReadOnlyList<SyncTombstone> Tombstones,
    DateTime ServerTimeUtc);

/// <param name="TokensBase64">Base64 of a JSON object entity → last token received; empty on first sync.</param>
public sealed record SyncPullQuery(string? TokensBase64, int Limit) : IQuery<Result<SyncPullResult>>;

/// <summary>
/// Section 12 pull: captures MIN_ACTIVE_ROWVERSION() once so no entity can skip a row that a
/// still-open transaction commits later, then asks each module's <see cref="ISyncSource"/> for
/// one page. The app repeats pulls until no entity reports hasMore.
/// </summary>
public sealed class SyncPullQueryHandler(
    IEnumerable<ISyncSource> sources, IUnitOfWork unitOfWork, ICurrentUserContext currentUser, IClock clock)
    : IQueryHandler<SyncPullQuery, Result<SyncPullResult>>
{
    public const int MaxLimit = 1000;
    private const string TombstonesKey = "tombstones";

    public async ValueTask<Result<SyncPullResult>> Handle(SyncPullQuery query, CancellationToken ct)
    {
        if (query.Limit is < 1 or > MaxLimit)
        {
            return Result.Failure<SyncPullResult>(Error.Validation($"limit must be between 1 and {MaxLimit}."));
        }

        if (!TryParseTokens(query.TokensBase64, out var tokens))
        {
            return Result.Failure<SyncPullResult>(Error.Validation("tokens must be base64-encoded JSON: { \"entity\": \"token\" }."));
        }

        var upper = RowVersionToken.FromBinary(await unitOfWork.Connection.ExecuteScalarAsync<byte[]>(new CommandDefinition(
            "SELECT CAST(MIN_ACTIVE_ROWVERSION() AS binary(8));", transaction: unitOfWork.Transaction, cancellationToken: ct))
            ?? throw new InvalidOperationException("MIN_ACTIVE_ROWVERSION() returned NULL."));

        var newTokens = new Dictionary<string, string>();
        var hasMore = new Dictionary<string, bool>();
        var changes = new Dictionary<string, IReadOnlyList<object>>();

        foreach (var source in sources)
        {
            var page = await source.PullAsync(
                new SyncPullRequest(tokens.GetValueOrDefault(source.Entity), upper, query.Limit, currentUser.UserId), ct);
            newTokens[source.Entity] = page.Token;
            hasMore[source.Entity] = page.HasMore;
            changes[source.Entity] = page.Rows;
        }

        var tombstoneRows = (await unitOfWork.Connection.QueryAsync<TombstoneRow>(new CommandDefinition(
            """
            SELECT TOP (@limit) EntityName, EntityId, RowVer
            FROM sync.Tombstone
            WHERE TenantId = @tenantId AND RowVer > @after AND RowVer < @upper
            ORDER BY RowVer;
            """,
            new
            {
                limit = query.Limit,
                tenantId = currentUser.TenantId,
                after = RowVersionToken.ToBinary(RowVersionToken.Parse(tokens.GetValueOrDefault(TombstonesKey))),
                upper = RowVersionToken.ToBinary(upper),
            },
            unitOfWork.Transaction,
            cancellationToken: ct))).ToList();

        newTokens[TombstonesKey] = RowVersionToken.Next(
            tombstoneRows.Select(t => RowVersionToken.FromBinary(t.RowVer)).ToList(), query.Limit, upper);
        hasMore[TombstonesKey] = tombstoneRows.Count >= query.Limit;

        return Result.Success(new SyncPullResult(
            newTokens,
            hasMore,
            changes,
            tombstoneRows.Select(t => new SyncTombstone(t.EntityName, t.EntityId)).ToList(),
            clock.UtcNow));
    }

    private static bool TryParseTokens(string? tokensBase64, out Dictionary<string, string> tokens)
    {
        tokens = [];
        if (string.IsNullOrWhiteSpace(tokensBase64))
        {
            return true;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(tokensBase64));
            tokens = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
            return true;
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return false;
        }
    }

    private sealed record TombstoneRow(string EntityName, Guid EntityId, byte[] RowVer);
}
