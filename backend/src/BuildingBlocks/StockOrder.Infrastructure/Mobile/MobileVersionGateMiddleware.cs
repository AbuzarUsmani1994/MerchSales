using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StockOrder.Infrastructure.Database;
using StockOrder.SharedKernel.Errors;

namespace StockOrder.Infrastructure.Mobile;

/// <summary>
/// Mobile requests send X-App-Version and X-Device-Id (section 9); this enforces the
/// minimum app version and device revocation before the request reaches a controller. Web
/// requests don't send these headers and are left untouched.
/// </summary>
public sealed class MobileVersionGateMiddleware(RequestDelegate next)
{
    // IBypassConnectionFactory is internal (section 6), so it's injected here as a method
    // parameter — which ASP.NET Core's convention-based middleware resolves per-request from
    // DI via reflection — rather than through the constructor, which would otherwise need to
    // be at least as accessible as this public class (CS0051).
    public async Task InvokeAsync(HttpContext context, IBypassConnectionFactory bypassFactory)
    {
        var appVersion = context.Request.Headers["X-App-Version"].ToString();
        var deviceIdHeader = context.Request.Headers["X-Device-Id"].ToString();

        if (string.IsNullOrWhiteSpace(appVersion) || !Guid.TryParse(deviceIdHeader, out var deviceId))
        {
            await next(context);
            return;
        }

        await using var connection = await bypassFactory.OpenAsync(context.RequestAborted);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.Platform, d.IsRevoked, d.WipeRequested, p.MinVersion
            FROM sync.Device d
            LEFT JOIN sync.AppVersionPolicy p ON p.Platform = d.Platform
            WHERE d.Id = @deviceId;
            """;
        command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@deviceId", deviceId));

        await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
        if (!await reader.ReadAsync(context.RequestAborted))
        {
            await next(context);
            return;
        }

        var isRevoked = reader.GetBoolean(1);
        var wipeRequested = reader.GetBoolean(2);
        var minVersion = reader.IsDBNull(3) ? null : reader.GetString(3);
        await reader.DisposeAsync();

        if (isRevoked)
        {
            await WriteProblemAsync(context, StatusCodes.Status401Unauthorized, ErrorCodes.DeviceRevoked,
                "This device has been revoked. Sign out.", ("wipeRequested", wipeRequested));
            return;
        }

        if (minVersion is not null && IsBelowMinimum(appVersion, minVersion))
        {
            await WriteProblemAsync(context, 426, ErrorCodes.AppVersionUnsupported,
                $"This app version ({appVersion}) is no longer supported. Minimum version is {minVersion}.");
            return;
        }

        await next(context);
    }

    private static bool IsBelowMinimum(string appVersion, string minVersion) =>
        Version.TryParse(appVersion, out var current) && Version.TryParse(minVersion, out var min) && current < min;

    private static async Task WriteProblemAsync(
        HttpContext context, int status, string code, string detail, (string Key, object Value)? extra = null)
    {
        var problem = new ProblemDetails
        {
            Type = $"https://errors.stockorder.app/{code}",
            Title = code,
            Status = status,
            Detail = detail,
        };
        problem.Extensions["code"] = code;
        if (extra is { } e)
        {
            problem.Extensions[e.Key] = e.Value;
        }

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problem, context.RequestAborted);
    }
}
