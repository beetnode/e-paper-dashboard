using EPaperDashboard.Data.LiteDb;
using EPaperDashboard.Data.PostgreSql;
using EPaperDashboard.Data.Repositories;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Conventions;
using FluentMigrator.Runner.Initialization;
using FluentMigrator.Runner.VersionTableInfo;

namespace EPaperDashboard.Data;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, DatabaseSettings settings)
    {
        services.AddSingleton(settings);

        switch (settings.Provider)
        {
            case DatabaseProvider.LiteDb:
                services.AddSingleton<LiteDbContext>();
                services.AddSingleton<IPersistenceInitializer>(sp => sp.GetRequiredService<LiteDbContext>());
                services.AddSingleton<IUserRepository, LiteDbUserRepository>();
                services.AddSingleton<IDashboardRepository, LiteDbDashboardRepository>();
                services.AddSingleton<IDeviceRepository, LiteDbDeviceRepository>();
                services.AddSingleton<IPairingSessionRepository, LiteDbPairingSessionRepository>();
                services.AddSingleton<IUnitOfWork, LiteDbUnitOfWork>();
                break;

            case DatabaseProvider.PostgreSql:
                services.AddSingleton<PostgresDbContext>();
                services.AddSingleton<IPersistenceInitializer, PostgresSchemaInitializer>();
                services.AddSingleton<IUserRepository, PostgresUserRepository>();
                services.AddSingleton<IDashboardRepository, PostgresDashboardRepository>();
                services.AddSingleton<IDeviceRepository, PostgresDeviceRepository>();
                services.AddSingleton<IPairingSessionRepository, PostgresPairingSessionRepository>();
                services.AddSingleton<IUnitOfWork, PostgresUnitOfWork>();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(settings), settings.Provider, "Unsupported database provider");
        }

        return services;
    }

    public static IServiceCollection AddDatabaseMaintenance(this IServiceCollection services, DatabaseSettings settings)
    {
        if (settings.Provider != DatabaseProvider.PostgreSql)
            throw new InvalidOperationException("Database maintenance migrations are available only for PostgreSQL");

        services.AddSingleton(settings);
        services.AddFluentMigratorCore()
            .ConfigureRunner(runner => runner
                .AddPostgres()
                .WithGlobalConnectionString(settings.ConnectionString!)
                .ScanIn(typeof(CreateInitialPostgresSchema).Assembly).For.Migrations());
        services.AddSingleton<IVersionTableMetaData, PostgresVersionTableMetaData>();
        services.AddSingleton<IConventionSet>(sp => new DefaultConventionSet(
            sp.GetRequiredService<IVersionTableMetaData>().SchemaName, null));
        services.Configure<TypeFilterOptions>(options =>
        {
            options.Namespace = typeof(CreateInitialPostgresSchema).Namespace;
            options.NestedNamespaces = true;
        });
        services.AddSingleton<IDatabaseMaintenance, PostgresDatabaseMaintenance>();
        return services;
    }
}
