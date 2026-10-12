namespace EPaperDashboard.Data.Repositories;

public interface IUnitOfWork
{
    T Execute<T>(Func<T> operation);

    /// <summary>
    /// Runs <paramref name="operation"/> in a transaction while holding the lock <paramref name="lockName"/>,
    /// which app instances sharing the database take in turn: others wait until the transaction ends.
    /// </summary>
    T ExecuteExclusive<T>(string lockName, Func<T> operation);
}
