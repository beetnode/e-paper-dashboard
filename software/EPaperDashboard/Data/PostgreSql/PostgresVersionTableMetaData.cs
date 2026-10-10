using FluentMigrator.Runner.VersionTableInfo;
using Npgsql;

namespace EPaperDashboard.Data.PostgreSql;

internal sealed class PostgresVersionTableMetaData : IVersionTableMetaData
{
    public PostgresVersionTableMetaData(DatabaseSettings settings)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(settings.ConnectionString!) { Pooling = false };
        using var connection = new NpgsqlConnection(connectionString.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand("SELECT current_schema()", connection);
        SchemaName = command.ExecuteScalar() as string
            ?? throw new InvalidOperationException("The PostgreSQL connection must have an existing schema in its search path");
    }

    public bool OwnsSchema => false;
    public string SchemaName { get; }
    public string TableName => "izboard_schema_versions";
    public string ColumnName => "version";
    public string DescriptionColumnName => "description";
    public string UniqueIndexName => "ux_izboard_schema_versions_version";
    public string AppliedOnColumnName => "applied_on";
    public bool CreateWithPrimaryKey => false;
}
