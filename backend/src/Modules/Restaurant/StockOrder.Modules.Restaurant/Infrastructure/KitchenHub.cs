using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using StockOrder.Infrastructure.Database;
using StockOrder.SharedKernel.Context;

namespace StockOrder.Modules.Restaurant.Infrastructure;

/// <summary>
/// Live updates for the kitchen screen (SignalR, section 2). A message only says "branch X's
/// board changed" and carries no order data: the screen then reloads the board through the
/// normal, permission-checked API. So the hub needs no database access (it runs outside the
/// request unit of work, see <see cref="UnitOfWorkMiddleware.HubsPath"/>) and only checks that
/// the caller is signed in; groups are per company and branch, from the token's tenant.
/// </summary>
[Authorize]
public sealed class KitchenHub : Hub
{
    public const string Path = UnitOfWorkMiddleware.HubsPath + "/kitchen";
    public const string BoardChanged = "boardChanged";

    /// <summary>Called by the kitchen screen for the branch it shows.</summary>
    public Task Watch(Guid branchId)
    {
        var tenant = Context.User?.FindFirst("tid")?.Value;
        if (!Guid.TryParse(tenant, out var tenantId) || branchId == Guid.Empty)
        {
            throw new HubException("Not signed in.");
        }

        return Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(tenantId, branchId));
    }

    public static string GroupFor(Guid tenantId, Guid branchId) => $"{tenantId:N}:{branchId:N}";
}

/// <summary>Collects the branches whose board changed in this request and tells their screens once it commits.</summary>
public interface IKitchenBoardNotifier
{
    void BranchChanged(Guid branchId);
}

internal sealed class KitchenBoardNotifier(IUnitOfWork unitOfWork, IHubContext<KitchenHub> hub, ICurrentUserContext currentUser)
    : IKitchenBoardNotifier
{
    private readonly HashSet<Guid> _branches = [];

    public void BranchChanged(Guid branchId)
    {
        if (_branches.Count == 0)
        {
            var tenantId = currentUser.TenantId;
            unitOfWork.OnCommitted(async () =>
            {
                foreach (var branch in _branches)
                {
                    await hub.Clients.Group(KitchenHub.GroupFor(tenantId, branch)).SendAsync(KitchenHub.BoardChanged, branch);
                }

                _branches.Clear();
            });
        }

        _branches.Add(branchId);
    }
}
