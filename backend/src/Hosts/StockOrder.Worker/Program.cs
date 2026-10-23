using Hangfire;
using Hangfire.SqlServer;
using Serilog;
using StockOrder.Infrastructure;
using StockOrder.Infrastructure.Database;
using StockOrder.Infrastructure.Logging;
using StockOrder.Infrastructure.Outbox;
using StockOrder.Infrastructure.Startup;
using StockOrder.Modules.Identity;
using StockOrder.Modules.Platform;
using StockOrder.Modules.Tenancy;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "StockOrder Worker");

Log.Logger = SerilogSetup.Configure(new LoggerConfiguration(), builder.Configuration).CreateLogger();
builder.Services.AddSerilog();

builder.Services.AddStockOrderInfrastructure(builder.Configuration);
builder.Services.AddTenancyModule();
builder.Services.AddIdentityModule();
builder.Services.AddPlatformModule();

var connectionString = builder.Configuration.GetConnectionString("Main")
    ?? throw new InvalidOperationException("Missing required configuration: ConnectionStrings:Main.");

// Schema is managed by database/scripts/012_Hangfire.sql, never by Hangfire itself at runtime (section 2, 15).
builder.Services.AddHangfire(config => config
    .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
    {
        SchemaName = "HangFire",
        PrepareSchemaIfNecessary = false,
    }));
builder.Services.AddHangfireServer();

builder.Services.AddOutboxDispatcher();

var host = builder.Build();

await using (var scope = host.Services.CreateAsyncScope())
{
    var databaseOptions = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>();
    await SchemaVersionChecker.EnsureAppliedAsync(databaseOptions, CancellationToken.None);
}

host.Run();
