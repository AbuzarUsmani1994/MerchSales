using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StockOrder.Modules.Catalog.Application;

namespace StockOrder.Modules.Catalog.Api;

/// <summary>
/// The customer menu a waiter's QR opens: anonymous on purpose (docs/decisions.md, "Public
/// customer menu"). Served as HTML from the API's own site, so the QR needs no other host.
/// </summary>
[Route("m")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicy)]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class PublicMenuController(IMediator mediator) : ControllerBase
{
    public const string RateLimitPolicy = "public-menu";

    [HttpGet("{code}")]
    public async Task<IActionResult> Menu(string code, CancellationToken ct)
    {
        var result = await mediator.Send(new GetPublicMenuQuery(code), ct);
        SetPageHeaders();
        if (result.IsFailure)
        {
            Response.Headers.CacheControl = "no-store";
            return new ContentResult { StatusCode = StatusCodes.Status404NotFound, ContentType = "text/html; charset=utf-8", Content = PublicMenuPage.RenderNotFound() };
        }

        // Short, so price changes reach customers within a minute.
        Response.Headers.CacheControl = "public, max-age=60";
        return new ContentResult { StatusCode = StatusCodes.Status200OK, ContentType = "text/html; charset=utf-8", Content = PublicMenuPage.Render(code, result.Value) };
    }

    [HttpGet("{code}/items/{productId:guid}/image")]
    public async Task<IActionResult> Image(string code, Guid productId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetPublicMenuImageQuery(code, productId), ct);
        if (result.IsFailure)
        {
            return NotFound();
        }

        // The URL carries the picture's id (?v=), which changes whenever the picture does.
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(result.Value.Content, result.Value.ContentType);
    }

    private void SetPageHeaders()
    {
        Response.Headers.ContentSecurityPolicy = PublicMenuPage.ContentSecurityPolicy;
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}
