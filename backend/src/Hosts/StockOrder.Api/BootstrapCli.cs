using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using StockOrder.Infrastructure;
using StockOrder.Infrastructure.Logging;
using StockOrder.Modules.Platform;
using StockOrder.Modules.Platform.Infrastructure.Bootstrap;

namespace StockOrder.Api;

/// <summary>`StockOrder.Api.exe bootstrap-platform --admin &lt;username&gt;` (section 10).</summary>
public static class BootstrapCli
{
    public static async Task RunAsync(string[] args)
    {
        var adminIndex = Array.IndexOf(args, "--admin");
        if (adminIndex < 0 || adminIndex == args.Length - 1)
        {
            Console.Error.WriteLine("Usage: StockOrder.Api.exe bootstrap-platform --admin <username>");
            Environment.ExitCode = 1;
            return;
        }

        var adminUsername = args[adminIndex + 1];

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        Log.Logger = SerilogSetup.Configure(new LoggerConfiguration(), configuration).CreateLogger();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(logging => logging.AddSerilog());
        services.AddStockOrderInfrastructure(configuration);
        services.AddPlatformModule();

        await using var provider = services.BuildServiceProvider();
        var bootstrapService = provider.GetRequiredService<IBootstrapPlatformService>();

        try
        {
            var result = await bootstrapService.BootstrapAsync(adminUsername, CancellationToken.None);

            Console.WriteLine();
            Console.WriteLine("Platform bootstrap complete.");
            Console.WriteLine($"  Tenant id:    {result.TenantId}");
            Console.WriteLine($"  Admin user:   {adminUsername} ({result.AdminUserId})");
            Console.WriteLine($"  One-time password: {result.OneTimePassword}");
            Console.WriteLine();
            Console.WriteLine("Log in with tenant code 'PLATFORM' and change this password immediately.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Bootstrap failed: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }
}
