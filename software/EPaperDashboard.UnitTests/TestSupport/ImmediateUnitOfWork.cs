using EPaperDashboard.Data.Repositories;

namespace EPaperDashboard.UnitTests.TestSupport;

internal sealed class ImmediateUnitOfWork : IUnitOfWork
{
    public static ImmediateUnitOfWork Instance { get; } = new();

    private ImmediateUnitOfWork() { }

    public T Execute<T>(Func<T> operation) => operation();

    public T ExecuteExclusive<T>(string lockName, Func<T> operation) => operation();
}
