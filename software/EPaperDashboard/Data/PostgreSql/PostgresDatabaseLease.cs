using Npgsql;

namespace EPaperDashboard.Data.PostgreSql;

// Normal services share this lock for their lifetime. Maintenance requires exclusive access.
internal sealed class PostgresDatabaseLease(NpgsqlConnection connection) : IDisposable
{
    private const long LockKey = 0x697a426f617264; // "izBoard"
    internal NpgsqlConnection Connection => connection;

    internal static PostgresDatabaseLease Acquire(DatabaseSettings settings, bool maintenance)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(settings.ConnectionString!) { Pooling = false };
        var connection = new NpgsqlConnection(connectionString.ConnectionString);
        try
        {
            connection.Open();
            var sql = maintenance ? "SELECT pg_try_advisory_lock(@key)" : "SELECT pg_try_advisory_lock_shared(@key)";
            using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("key", LockKey);
            if (command.ExecuteScalar() is not true)
                throw new InvalidOperationException(maintenance
                    ? "Stop all app instances before running database maintenance; the database is in use"
                    : "Database maintenance is in progress; the service cannot start");
            return new PostgresDatabaseLease(connection);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public void Dispose() => connection.Dispose();
}
