using System.Reflection;
using FluentMigrator;
using Npgsql;

namespace EPaperDashboard.Data.PostgreSql;

internal static class PostgresSchemaState
{
    private static readonly long[] ExpectedVersions = typeof(CreateInitialPostgresSchema).Assembly.GetTypes()
        .Where(type => type.Namespace == typeof(CreateInitialPostgresSchema).Namespace ||
            type.Namespace?.StartsWith(typeof(CreateInitialPostgresSchema).Namespace + ".", StringComparison.Ordinal) == true)
        .Select(type => type.GetCustomAttribute<MigrationAttribute>())
        .OfType<MigrationAttribute>().Select(attribute => attribute.Version).Order().ToArray();

    internal static void EnsureCurrent(NpgsqlConnection connection)
    {
        var applied = ReadAppliedVersions(connection);
        RejectUnknownVersions(applied);
        if (applied is null || !applied.SequenceEqual(ExpectedVersions))
            throw new InvalidOperationException(
                "PostgreSQL migrations are required. Stop all app instances and run this version with --maintenance before starting the service");
    }

    internal static void EnsureUpgradeable(NpgsqlConnection connection) => RejectUnknownVersions(ReadAppliedVersions(connection));

    private static void RejectUnknownVersions(long[]? applied)
    {
        if (applied?.Except(ExpectedVersions).Any() == true)
            throw new InvalidOperationException("PostgreSQL contains schema migrations unsupported by this app version; use the matching app release");
    }

    private static long[]? ReadAppliedVersions(NpgsqlConnection connection)
    {
        using var schemaCommand = new NpgsqlCommand("SELECT current_schema()", connection);
        var schema = schemaCommand.ExecuteScalar() as string
            ?? throw new InvalidOperationException("The PostgreSQL connection must have an existing schema in its search path");
        using var quoter = new NpgsqlCommandBuilder();
        var table = quoter.QuoteIdentifier(schema) + ".izboard_schema_versions";
        using var exists = new NpgsqlCommand("SELECT to_regclass(@table) IS NOT NULL", connection);
        exists.Parameters.AddWithValue("table", table);
        if (exists.ExecuteScalar() is not true) return null;

        using var versions = new NpgsqlCommand($"SELECT version FROM {table} ORDER BY version", connection);
        using var reader = versions.ExecuteReader();
        var applied = new List<long>();
        while (reader.Read()) applied.Add(reader.GetInt64(0));
        return applied.ToArray();
    }
}
