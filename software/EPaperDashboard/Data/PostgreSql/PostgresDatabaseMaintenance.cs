using FluentMigrator.Runner;

namespace EPaperDashboard.Data.PostgreSql;

internal sealed class PostgresDatabaseMaintenance(IServiceScopeFactory scopeFactory, DatabaseSettings settings) : IDatabaseMaintenance
{
    public void Migrate()
    {
        using var lease = PostgresDatabaseLease.Acquire(settings, maintenance: true);
        PostgresSchemaState.EnsureUpgradeable(lease.Connection);
        // Resolve the runner only after obtaining exclusive access to the database.
        using var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
        PostgresSchemaState.EnsureCurrent(lease.Connection);
    }
}
