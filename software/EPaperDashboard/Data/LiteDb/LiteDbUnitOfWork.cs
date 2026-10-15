using System.Collections.Concurrent;
using EPaperDashboard.Data.Repositories;

namespace EPaperDashboard.Data.LiteDb;

internal sealed class LiteDbUnitOfWork(LiteDbContext context) : IUnitOfWork
{
    public T Execute<T>(Func<T> operation)
    {
        context.BeginTransaction();
        try
        {
            var result = operation();
            context.Commit();
            return result;
        }
        catch
        {
            context.Rollback();
            throw;
        }
    }

    private readonly ConcurrentDictionary<string, object> _locks = new();

    // A LiteDB file belongs to one app instance, so an in-process lock excludes every other writer. The transaction
    // alone is not enough: LiteDB lets concurrent transactions read before either writes.
    public T ExecuteExclusive<T>(string lockName, Func<T> operation)
    {
        lock (_locks.GetOrAdd(lockName, _ => new object()))
        {
            return Execute(operation);
        }
    }
}
