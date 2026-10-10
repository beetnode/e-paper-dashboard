using FluentMigrator;
using FluentMigrator.Postgres;
using FluentMigrator.Runner.VersionTableInfo;

namespace EPaperDashboard.Data.PostgreSql;

[Migration(1)]
public sealed class CreateInitialPostgresSchema(IVersionTableMetaData versionTable) : Migration
{
    public override void Up()
    {
        // Preserve the schema and data created by earlier PostgreSQL builds.
        if (!Schema.Schema(versionTable.SchemaName).Table("izboard_users").Exists())
            Create.Table("izboard_users").InSchema(versionTable.SchemaName)
                .WithColumn("id").AsCustom("text").NotNullable().PrimaryKey()
                .WithColumn("username").AsCustom("text").NotNullable().Unique()
                .WithColumn("is_super_user").AsBoolean().NotNullable()
                .WithColumn("document").AsCustom("jsonb").NotNullable();

        if (!Schema.Schema(versionTable.SchemaName).Table("izboard_dashboards").Exists())
            Create.Table("izboard_dashboards").InSchema(versionTable.SchemaName)
                .WithColumn("id").AsCustom("text").NotNullable().PrimaryKey()
                .WithColumn("user_id").AsCustom("text").NotNullable()
                .WithColumn("document").AsCustom("jsonb").NotNullable();

        if (!Schema.Schema(versionTable.SchemaName).Table("izboard_devices").Exists())
            Create.Table("izboard_devices").InSchema(versionTable.SchemaName)
                .WithColumn("id").AsCustom("text").NotNullable().PrimaryKey()
                .WithColumn("user_id").AsCustom("text").NotNullable()
                .WithColumn("dashboard_id").AsCustom("text").NotNullable()
                .WithColumn("device_identifier").AsCustom("text").NotNullable()
                .WithColumn("api_key").AsCustom("text").NotNullable()
                .WithColumn("document").AsCustom("jsonb").NotNullable();

        if (!Schema.Schema(versionTable.SchemaName).Table("izboard_pairing_sessions").Exists())
            Create.Table("izboard_pairing_sessions").InSchema(versionTable.SchemaName)
                .WithColumn("id").AsCustom("text").NotNullable().PrimaryKey()
                .WithColumn("code").AsCustom("text").NotNullable().Unique()
                .WithColumn("expires_at").AsCustom("timestamptz").NotNullable()
                .WithColumn("is_completed").AsBoolean().NotNullable()
                .WithColumn("document").AsCustom("jsonb").NotNullable();

        CreateIndex("izboard_dashboards", "user_id", "ix_izboard_dashboards_user_id");
        CreateIndex("izboard_devices", "user_id", "ix_izboard_devices_user_id");
        CreateIndex("izboard_devices", "dashboard_id", "ix_izboard_devices_dashboard_id");
        CreateIndex("izboard_pairing_sessions", "expires_at", "ix_izboard_pairing_sessions_expiry");

        if (!Schema.Schema(versionTable.SchemaName).Table("izboard_devices").Index("ux_izboard_devices_identifier").Exists())
            Create.Index("ux_izboard_devices_identifier").OnTable("izboard_devices").InSchema(versionTable.SchemaName)
                .OnColumn("device_identifier").Ascending().WithOptions().Filter("device_identifier <> ''").Unique();
        if (!Schema.Schema(versionTable.SchemaName).Table("izboard_devices").Index("ux_izboard_devices_api_key").Exists())
            Create.Index("ux_izboard_devices_api_key").OnTable("izboard_devices").InSchema(versionTable.SchemaName)
                .OnColumn("api_key").Ascending().WithOptions().Filter("api_key <> ''").Unique();
    }

    private void CreateIndex(string table, string column, string name)
    {
        if (!Schema.Schema(versionTable.SchemaName).Table(table).Index(name).Exists())
            Create.Index(name).OnTable(table).InSchema(versionTable.SchemaName).OnColumn(column).Ascending();
    }

    public override void Down() =>
        throw new NotSupportedException("Rolling back the initial PostgreSQL schema would delete application data");
}
