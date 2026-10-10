using EPaperDashboard.Data.Repositories;

namespace EPaperDashboard.Data.PostgreSql;

internal sealed class PostgresUnitOfWork(PostgresDbContext context) : IUnitOfWork
{
    public T Execute<T>(Func<T> operation) => context.ExecuteInTransaction(operation);
}
