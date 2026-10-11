using FluentMigrator;
using FluentMigrator.Runner.VersionTableInfo;

namespace EPaperDashboard.Data.PostgreSql;

[Migration(2)]
public sealed class AddServiceSetupState(IVersionTableMetaData versionTable) : Migration
{
    public override void Up()
    {
        if (!Schema.Schema(versionTable.SchemaName).Table("izboard_service_state").Exists())
            Create.Table("izboard_service_state").InSchema(versionTable.SchemaName)
                .WithColumn("id").AsCustom("text").NotNullable().PrimaryKey()
                .WithColumn("enabled").AsBoolean().Nullable();
        // Record upgrades even if all accounts are later removed.
        var schema = "\"" + versionTable.SchemaName.Replace("\"", "\"\"") + "\"";
        Execute.Sql($"INSERT INTO {schema}.izboard_service_state (id) SELECT 'setup' WHERE EXISTS (SELECT 1 FROM {schema}.izboard_users) ON CONFLICT (id) DO NOTHING");
    }

    public override void Down() => throw new NotSupportedException("Removing setup state would reopen administrator registration.");
}
