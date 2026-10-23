using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Tenancy.Domain;
using StockOrder.Modules.Tenancy.Infrastructure;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Tenancy.Application.Settings;

public sealed record SettingDto(string SettingKey, string Value);

public sealed record GetSettingsQuery : IQuery<Result<IReadOnlyList<SettingDto>>>;

public sealed class GetSettingsQueryHandler(TenancyDbContext db, ICurrentUserContext currentUser)
    : IQueryHandler<GetSettingsQuery, Result<IReadOnlyList<SettingDto>>>
{
    public async ValueTask<Result<IReadOnlyList<SettingDto>>> Handle(GetSettingsQuery query, CancellationToken ct)
    {
        var settings = await db.TenantSettings
            .Where(s => s.TenantId == currentUser.TenantId)
            .OrderBy(s => s.SettingKey)
            .Select(s => new SettingDto(s.SettingKey, s.Value))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<SettingDto>>(settings);
    }
}

public sealed record SetSettingCommand(string SettingKey, string Value) : ICommand<Result>;

public sealed class SetSettingCommandValidator : AbstractValidator<SetSettingCommand>
{
    public SetSettingCommandValidator()
    {
        RuleFor(x => x.SettingKey).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Value).NotNull();
    }
}

public sealed class SetSettingCommandHandler(TenancyDbContext db, ICurrentUserContext currentUser, IClock clock)
    : ICommandHandler<SetSettingCommand, Result>
{
    public async ValueTask<Result> Handle(SetSettingCommand command, CancellationToken ct)
    {
        var setting = await db.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == currentUser.TenantId && s.SettingKey == command.SettingKey, ct);

        if (setting is null)
        {
            setting = TenantSetting.Create(currentUser.TenantId, command.SettingKey, command.Value, clock.UtcNow, currentUser.UserId);
            db.TenantSettings.Add(setting);
        }
        else
        {
            setting.SetValue(command.Value, clock.UtcNow, currentUser.UserId);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
