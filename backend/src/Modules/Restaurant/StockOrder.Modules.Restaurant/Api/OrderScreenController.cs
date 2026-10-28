using System.Text.Json;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StockOrder.Modules.Restaurant.Application;

namespace StockOrder.Modules.Restaurant.Api;

/// <summary>
/// The customer order screen on the hall TV: anonymous on purpose, like the customer menu
/// (docs/decisions.md "Tables and kitchen screen"). The page polls its own small JSON every few
/// seconds; it shows order numbers only.
/// </summary>
[Route("s")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicy)]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class OrderScreenController(IMediator mediator) : ControllerBase
{
    /// <summary>Shared with the customer menu: per IP, generous, because guests share the restaurant's Wi-Fi.</summary>
    public const string RateLimitPolicy = "public-menu";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [HttpGet("screen.js")]
    public IActionResult Script()
    {
        Response.Headers.CacheControl = "public, max-age=3600";
        Response.Headers.XContentTypeOptions = "nosniff";
        return Content(OrderScreenPage.Script, "text/javascript; charset=utf-8");
    }

    [HttpGet("{code}")]
    public async Task<IActionResult> Screen(string code, CancellationToken ct)
    {
        var result = await mediator.Send(new GetOrderScreenQuery(code), ct);
        SetPageHeaders();
        Response.Headers.CacheControl = "no-store";
        return result.IsFailure
            ? new ContentResult { StatusCode = StatusCodes.Status404NotFound, ContentType = "text/html; charset=utf-8", Content = OrderScreenPage.RenderNotFound() }
            : new ContentResult { StatusCode = StatusCodes.Status200OK, ContentType = "text/html; charset=utf-8", Content = OrderScreenPage.Render(result.Value) };
    }

    [HttpGet("{code}/data")]
    public async Task<IActionResult> Data(string code, CancellationToken ct)
    {
        var result = await mediator.Send(new GetOrderScreenQuery(code), ct);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        if (result.IsFailure)
        {
            return NotFound();
        }

        return Content(JsonSerializer.Serialize(new { preparing = result.Value.Preparing, ready = result.Value.Ready }, Json), "application/json");
    }

    private void SetPageHeaders()
    {
        Response.Headers.ContentSecurityPolicy = OrderScreenPage.ContentSecurityPolicy;
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}
