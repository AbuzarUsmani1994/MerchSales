using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Identity.Contracts;

namespace StockOrder.Modules.Identity.Infrastructure;

internal sealed class UserDirectory(IdentityDbContext db) : IUserDirectory
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var ids = userIds.Distinct().ToList();
        return await db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }
}
