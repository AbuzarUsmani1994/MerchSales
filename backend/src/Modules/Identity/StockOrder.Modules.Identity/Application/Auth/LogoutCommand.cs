using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Identity.Infrastructure;
using StockOrder.Modules.Identity.Infrastructure.Auth;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Identity.Application.Auth;

public sealed record LogoutCommand(string RefreshToken) : ICommand<Result>;

public sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator() => RuleFor(x => x.RefreshToken).NotEmpty();
}

public sealed class LogoutCommandHandler(IdentityDbContext db, ICurrentUserContext currentUser, IClock clock)
    : ICommandHandler<LogoutCommand, Result>
{
    public async ValueTask<Result> Handle(LogoutCommand command, CancellationToken ct)
    {
        var hash = RefreshTokenFactory.Hash(command.RefreshToken);
        var now = clock.UtcNow;

        var familyId = await db.RefreshTokens
            .Where(t => t.TenantId == currentUser.TenantId && t.TokenHash == hash && t.UserId == currentUser.UserId)
            .Select(t => t.FamilyId)
            .FirstOrDefaultAsync(ct);

        if (familyId != Guid.Empty)
        {
            await db.RefreshTokens
                .Where(t => t.TenantId == currentUser.TenantId && t.FamilyId == familyId && t.RevokedAtUtc == null)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(t => t.RevokedAtUtc, now)
                        .SetProperty(t => t.RevokedReason, "LoggedOut"),
                    ct);
        }

        return Result.Success();
    }
}
