using Microsoft.AspNetCore.Http;
using StockOrder.SharedKernel.Context;

namespace StockOrder.Infrastructure.Database;

/// <summary>
/// Commits the request's transaction on a successful (&lt; 400) response, rolls it back
/// otherwise — including on an unhandled exception. Runs after authentication, so
/// <see cref="ICurrentUserContext"/> already has the tenant from the validated token.
/// </summary>
public sealed class UnitOfWorkMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IUnitOfWork unitOfWork, ICurrentUserContext currentUser)
    {
        if (!currentUser.IsAuthenticated)
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
