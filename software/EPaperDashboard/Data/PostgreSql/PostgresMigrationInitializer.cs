using FluentMigrator.Runner;
using Npgsql;

namespace EPaperDashboard.Data.PostgreSql;

internal sealed class PostgresMigrationInitializer(IServiceScopeFactory scopeFactory, DatabaseSettings settings) : IPersistenceInitializer
{
    private const long MigrationLockKey = 0x697a426f617264; // "izBoard"

    public void Initialize()
    {
        // Acquire the lock before resolving the runner, which loads the migration history.
        // A dedicated, unpooled session releases the lock even when migration execution fails.
        var lockConnectionString = new NpgsqlConnectionStringBuilder(settings.ConnectionString!) { Pooling = false };
        using var lockConnection = new NpgsqlConnection(lockConnectionString.ConnectionString);
        lockConnection.Open();
        using var lockCommand = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", lockConnection);
        lockCommand.Parameters.AddWithValue("key", MigrationLockKey);
        lockCommand.ExecuteNonQuery();

        using var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }
}
