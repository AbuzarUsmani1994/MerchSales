using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockOrder.Infrastructure.Correlation;
using StockOrder.SharedKernel.Errors;

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
        if (exception is DbUpdateConcurrencyException)
        {
            // Someone else changed the row between our read and write (rowversion, section 9): the
            // request lost the race, which is the client's 412, not a server fault.
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Concurrency conflict. TraceId={TraceId}", correlationId.CorrelationId);
            }

            var conflict = new ProblemDetails
            {
                Type = $"https://errors.stockorder.app/{ErrorCodes.ConcurrencyConflict}",
                Title = ErrorCodes.ConcurrencyConflict,
                Status = StatusCodes.Status412PreconditionFailed,
                Detail = "Someone else changed this record at the same time. Reload and try again.",
            };
            conflict.Extensions["code"] = ErrorCodes.ConcurrencyConflict;
            conflict.Extensions["traceId"] = correlationId.CorrelationId;
            httpContext.Response.StatusCode = StatusCodes.Status412PreconditionFailed;
            await httpContext.Response.WriteAsJsonAsync(conflict, cancellationToken);
            return true;
        }

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
