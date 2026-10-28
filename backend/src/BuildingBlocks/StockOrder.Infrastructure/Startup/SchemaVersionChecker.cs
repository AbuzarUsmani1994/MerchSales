using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using StockOrder.Infrastructure.Database;

namespace StockOrder.Infrastructure.Startup;

/// <summary>
/// At startup, reads dbo.SchemaVersion and refuses to start if any script the code expects
/// is missing (section 7). <see cref="ExpectedScripts"/> is the one place that list lives.
/// </summary>
public static class SchemaVersionChecker
{
    public static readonly IReadOnlyList<string> ExpectedScripts =
    [
        "001_Foundation",
        "002_Identity",
        "003_Catalog",
        "004_Inventory",
        "005_Purchasing",
        "006_Sales",
        "007_Finance",
        "008_Sync",
        "009_RowLevelSecurity",
        "010_AppDatabaseRole",
        "011_SeedReferenceData",
        "012_Hangfire",
        "013_SelfServicePermission",
        "014_BranchCatalog",
        "015_Loyalty",
        "016_RestaurantPack",
    ];

    public static async Task EnsureAppliedAsync(IOptions<DatabaseOptions> options, CancellationToken ct)
    {
        await using var connection = new SqlConnection(options.Value.ConnectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ScriptName FROM dbo.SchemaVersion;";

        var applied = new HashSet<string>(StringComparer.Ordinal);
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                applied.Add(reader.GetString(0));
            }
        }

        var missing = ExpectedScripts.Where(s => !applied.Contains(s)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"The database is missing required scripts: {string.Join(", ", missing)}. " +
                "Run database/scripts in order before starting the API (see database/scripts/README.md).");
        }
    }
}
