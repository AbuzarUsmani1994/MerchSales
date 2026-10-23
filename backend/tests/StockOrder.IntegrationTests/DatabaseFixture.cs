using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit;

namespace StockOrder.IntegrationTests;

/// <summary>
/// Section 19: "creates a fresh database, runs scripts 000-latest exactly as the user
/// would (same files), creates the app_runtime login/user, and runs tests as that login
/// (so RLS and DENY permissions apply)." Uses Testcontainers in CI
/// (mcr.microsoft.com/mssql/server:2019-latest); set TEST_SQL_CONNECTION locally to point
/// at an already-running SQL-auth-enabled SQL Server instead of starting a container.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private const string AppLogin = "app_test";
    private const string AppPassword = "St0ckOrder!TestLogin#2026";

    private MsSqlContainer? _container;
    private string _masterConnectionString = string.Empty;
    private string _databaseName = string.Empty;

    public string AppRuntimeConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var externalConnectionString = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION");

        if (!string.IsNullOrWhiteSpace(externalConnectionString))
        {
            _masterConnectionString = externalConnectionString;
        }
        else
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2019-latest").Build();
            await _container.StartAsync();
            _masterConnectionString = _container.GetConnectionString();
        }

        _databaseName = $"StockOrderTest_{Guid.NewGuid():N}";
        await RunScriptsAsync();
        await CreateAppLoginAsync();
        AppRuntimeConnectionString = BuildConnectionString(_databaseName, AppLogin, AppPassword);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    private async Task RunScriptsAsync()
    {
        var scriptsDir = Path.Combine(AppContext.BaseDirectory, "Scripts");
        var scripts = Directory.GetFiles(scriptsDir, "*.sql").OrderBy(f => f, StringComparer.Ordinal).ToList();

        foreach (var scriptPath in scripts)
        {
            var sql = await File.ReadAllTextAsync(scriptPath);
            sql = sql.Replace("StockOrderPlatform", _databaseName, StringComparison.Ordinal);

            // 000_CreateDatabase.sql runs against master; every other script runs against the
            // just-created database — same as the README's run instructions.
            var targetDatabase = scriptPath.Contains("000_CreateDatabase", StringComparison.Ordinal)
                ? "master"
                : _databaseName;

            await using var connection = new SqlConnection(BuildConnectionString(targetDatabase, null, null));
            await connection.OpenAsync();

            foreach (var batch in SplitBatches(sql))
            {
                if (string.IsNullOrWhiteSpace(batch))
                {
                    continue;
                }

                await using var command = connection.CreateCommand();
                command.CommandTimeout = 120;
                command.CommandText = batch;
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    private async Task CreateAppLoginAsync()
    {
        await using var masterConnection = new SqlConnection(BuildConnectionString("master", null, null));
        await masterConnection.OpenAsync();
        await using (var loginCommand = masterConnection.CreateCommand())
        {
            loginCommand.CommandText = $"""
                IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = '{AppLogin}')
                    CREATE LOGIN [{AppLogin}] WITH PASSWORD = N'{AppPassword}', CHECK_POLICY = OFF;
                """;
            await loginCommand.ExecuteNonQueryAsync();
        }

        await using var dbConnection = new SqlConnection(BuildConnectionString(_databaseName, null, null));
        await dbConnection.OpenAsync();
        await using var userCommand = dbConnection.CreateCommand();
        userCommand.CommandText = $"""
            IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = '{AppLogin}')
                CREATE USER [{AppLogin}] FOR LOGIN [{AppLogin}];
            ALTER ROLE [app_runtime] ADD MEMBER [{AppLogin}];
            """;
        await userCommand.ExecuteNonQueryAsync();
    }

    private static List<string> SplitBatches(string sql)
    {
        var batches = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var line in sql.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                batches.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.AppendLine(line);
            }
        }

        batches.Add(current.ToString());
        return batches;
    }

    private string BuildConnectionString(string database, string? login, string? password)
    {
        if (login is null)
        {
            var builder = new SqlConnectionStringBuilder(_masterConnectionString) { InitialCatalog = database };
            return builder.ConnectionString;
        }

        var appBuilder = new SqlConnectionStringBuilder(_masterConnectionString)
        {
            InitialCatalog = database,
            UserID = login,
            Password = password,
            IntegratedSecurity = false,
        };
        return appBuilder.ConnectionString;
    }
}

[CollectionDefinition("Database")]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>;
