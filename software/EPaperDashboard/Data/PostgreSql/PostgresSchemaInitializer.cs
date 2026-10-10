namespace EPaperDashboard.Data.PostgreSql;

internal sealed class PostgresSchemaInitializer(DatabaseSettings settings) : IPersistenceInitializer, IDisposable
{
    private PostgresDatabaseLease? _lease;

    public void Initialize()
    {
        if (_lease is not null) return;
        var lease = PostgresDatabaseLease.Acquire(settings, maintenance: false);
        try
        {
            PostgresSchemaState.EnsureCurrent(lease.Connection);
            _lease = lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    public void Dispose() => _lease?.Dispose();
}
