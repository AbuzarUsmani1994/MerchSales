using Microsoft.EntityFrameworkCore;

namespace StockOrder.Infrastructure.Database;

/// <summary>
/// The module DbContexts created in this request. Rolling back to a savepoint (one sync command
/// in a push) undoes the database writes but not EF's change trackers: entities a failed command
/// added would be inserted by the next command's SaveChanges. Clearing them after the rollback
/// keeps each command isolated.
/// </summary>
public sealed class ScopedDbContexts
{
    private readonly List<DbContext> _contexts = [];

    internal void Add(DbContext context) => _contexts.Add(context);

    public void ClearChangeTrackers()
    {
        foreach (var context in _contexts)
        {
            context.ChangeTracker.Clear();
        }
    }
}
