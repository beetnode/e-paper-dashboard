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

    // A LiteDB file belongs to one app instance, so a transaction is already exclusive.
    public T ExecuteExclusive<T>(string lockName, Func<T> operation) => Execute(operation);
}
