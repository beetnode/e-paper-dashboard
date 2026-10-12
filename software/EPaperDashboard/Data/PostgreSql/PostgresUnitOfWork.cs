using System.Security.Cryptography;
using System.Text;
using EPaperDashboard.Data.Repositories;

namespace EPaperDashboard.Data.PostgreSql;

internal sealed class PostgresUnitOfWork(PostgresDbContext context) : IUnitOfWork
{
    public T Execute<T>(Func<T> operation) => context.ExecuteInTransaction(operation);

    public T ExecuteExclusive<T>(string lockName, Func<T> operation) => context.ExecuteInTransaction(() =>
    {
        // Released when the transaction ends, including when the connection drops.
        context.Execute("SELECT pg_advisory_xact_lock(@key)", PostgresDbContext.Parameter("key", LockKey(lockName)));
        return operation();
    });

    internal static long LockKey(string lockName) =>
        BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes("izboard:" + lockName)));
}
