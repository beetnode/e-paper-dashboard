using FluentMigrator;
using FluentMigrator.Runner.VersionTableInfo;

namespace EPaperDashboard.Data.PostgreSql;

/// <summary>Stores settings of owners without an account record, such as the Home Assistant add-on user.</summary>
[Migration(4)]
public sealed class AddServiceStateDocument(IVersionTableMetaData versionTable) : Migration
{
    public override void Up()
    {
        if (!Schema.Schema(versionTable.SchemaName).Table("izboard_service_state").Column("document").Exists())
            Alter.Table("izboard_service_state").InSchema(versionTable.SchemaName)
                .AddColumn("document").AsCustom("jsonb").Nullable();
    }

    public override void Down() => throw new NotSupportedException("Removing service state documents would delete stored settings.");
}
