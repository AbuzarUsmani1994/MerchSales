using Microsoft.AspNetCore.Http;
using StockOrder.SharedKernel.Context;

namespace StockOrder.Infrastructure.Database;

/// <summary>
/// Commits the request's transaction on a successful (&lt; 400) response, rolls it back
/// otherwise — including on an unhandled exception. Runs after authentication, so
/// <see cref="ICurrentUserContext"/> already has the tenant from the validated token.
/// Live-update connections (<see cref="HubsPath"/>) are skipped: a kitchen screen keeps one open
/// for hours, and must not hold a database connection and transaction all that time.
/// </summary>
public sealed class UnitOfWorkMiddleware(RequestDelegate next)
{
    public const string HubsPath = "/hubs";

    public async Task InvokeAsync(HttpContext context, IUnitOfWork unitOfWork, ICurrentUserContext currentUser)
    {
        if (!currentUser.IsAuthenticated || context.Request.Path.StartsWithSegments(HubsPath))
        {
            await next(context);
            return;
        }

        await unitOfWork.InitializeAsync(currentUser.TenantId, context.RequestAborted);

        try
        {
            await next(context);

            if (context.Response.StatusCode is >= 200 and < 400)
            {
                await unitOfWork.CommitAsync(context.RequestAborted);
            }
            else
            {
                await unitOfWork.RollbackAsync(context.RequestAborted);
            }
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
