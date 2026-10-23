using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using StockOrder.Infrastructure.Correlation;

namespace StockOrder.Infrastructure.Api;

/// <summary>
/// Catches exceptions that escape a handler — bugs and infrastructure failures (section 5.6)
/// — and turns them into the same ProblemDetails shape as an expected <c>Result</c> failure,
/// with <c>traceId</c> set to the request's correlation id (section 9).
/// </summary>
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    ICorrelationIdAccessor correlationId) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception. TraceId={TraceId}", correlationId.CorrelationId);

        var problem = new ProblemDetails
        {
            Type = "https://errors.stockorder.app/INTERNAL_ERROR",
            Title = "INTERNAL_ERROR",
            Status = StatusCodes.Status500InternalServerError,
            Detail = "An unexpected error occurred.",
        };
        problem.Extensions["code"] = "INTERNAL_ERROR";
        problem.Extensions["traceId"] = correlationId.CorrelationId;

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
