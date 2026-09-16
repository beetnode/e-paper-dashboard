using EPaperDashboard.Data.LiteDb;
using EPaperDashboard.Data.PostgreSql;
using EPaperDashboard.Data.Repositories;

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
                services.AddSingleton<IPersistenceInitializer>(sp => sp.GetRequiredService<PostgresDbContext>());
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
}
