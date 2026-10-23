using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;

namespace StockOrder.Infrastructure.Logging;

/// <summary>
/// Serilog to Seq and rolling files, 14-day retention (section 17). Never logs passwords,
/// tokens, PINs, full auth request bodies, or personal data beyond ids — callers are
/// responsible for not passing those as log properties in the first place.
/// </summary>
public static class SerilogSetup
{
    public static LoggerConfiguration Configure(LoggerConfiguration loggerConfiguration, IConfiguration configuration)
    {
        loggerConfiguration
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "StockOrder")
            .WriteTo.Console()
            .WriteTo.File(
                "logs/stockorder-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14);

        // Serilog.Sinks.Seq constructs its HTTP client eagerly, so — unlike MinimumLevel
        // filtering — this has to be an "add the sink or don't" decision, not a per-event
        // Conditional wrapper, or an empty Seq:Url throws at startup instead of just disabling it.
        var seqUrl = configuration["Seq:Url"];
        if (!string.IsNullOrWhiteSpace(seqUrl))
        {
            loggerConfiguration.WriteTo.Seq(seqUrl);
        }

        return loggerConfiguration;
    }
}
