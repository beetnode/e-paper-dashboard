using FluentMigrator;
using FluentMigrator.Runner.VersionTableInfo;
namespace EPaperDashboard.Data.PostgreSql;

[Migration(3)]
public sealed class AddProviderConnections(IVersionTableMetaData versionTable) : Migration
{
    public override void Up()
    {
        if (!Schema.Schema(versionTable.SchemaName).Table("izboard_provider_connections").Exists())
            Create.Table("izboard_provider_connections").InSchema(versionTable.SchemaName)
            .WithColumn("id").AsCustom("text").NotNullable().PrimaryKey()
            .WithColumn("user_id").AsCustom("text").NotNullable()
            .WithColumn("document").AsCustom("jsonb").NotNullable();
        if (!Schema.Schema(versionTable.SchemaName).Table("izboard_provider_connections").Index("ix_izboard_provider_connections_user_id").Exists())
            Create.Index("ix_izboard_provider_connections_user_id").OnTable("izboard_provider_connections")
            .InSchema(versionTable.SchemaName).OnColumn("user_id").Ascending();
    }
    public override void Down() => throw new NotSupportedException("Removing provider connections would delete user credentials.");
}
