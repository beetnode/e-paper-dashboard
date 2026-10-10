using FluentMigrator;

namespace EPaperDashboard.Data.PostgreSql;

// The IF NOT EXISTS clauses also baseline databases created before schema versioning was added.
[Migration(1)]
public sealed class CreateInitialPostgresSchema : Migration
{
    public override void Up()
    {
        Execute.Sql("""
            CREATE TABLE IF NOT EXISTS izboard_users (
                id text PRIMARY KEY,
                username text NOT NULL UNIQUE,
                is_super_user boolean NOT NULL,
                document jsonb NOT NULL
            );
            CREATE TABLE IF NOT EXISTS izboard_dashboards (
                id text PRIMARY KEY,
                user_id text NOT NULL,
                document jsonb NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_izboard_dashboards_user_id
                ON izboard_dashboards (user_id);
            CREATE TABLE IF NOT EXISTS izboard_devices (
                id text PRIMARY KEY,
                user_id text NOT NULL,
                dashboard_id text NOT NULL,
                device_identifier text NOT NULL,
                api_key text NOT NULL,
                document jsonb NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_izboard_devices_user_id
                ON izboard_devices (user_id);
            CREATE INDEX IF NOT EXISTS ix_izboard_devices_dashboard_id
                ON izboard_devices (dashboard_id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_izboard_devices_identifier
                ON izboard_devices (device_identifier) WHERE device_identifier <> '';
            CREATE UNIQUE INDEX IF NOT EXISTS ux_izboard_devices_api_key
                ON izboard_devices (api_key) WHERE api_key <> '';
            CREATE TABLE IF NOT EXISTS izboard_pairing_sessions (
                id text PRIMARY KEY,
                code text NOT NULL UNIQUE,
                expires_at timestamptz NOT NULL,
                is_completed boolean NOT NULL,
                document jsonb NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_izboard_pairing_sessions_expiry
                ON izboard_pairing_sessions (expires_at);
            """);
    }

    public override void Down() =>
        throw new NotSupportedException("Rolling back the initial PostgreSQL schema would delete application data");
}
