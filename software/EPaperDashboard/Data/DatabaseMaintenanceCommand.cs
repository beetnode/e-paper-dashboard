using EPaperDashboard.Utilities;

namespace EPaperDashboard.Data;

internal static class DatabaseMaintenanceCommand
{
    internal static int Run(IEnvironmentConfiguration configuration)
    {
        try
        {
            var settings = DatabaseSettings.Parse(configuration.DatabaseProvider, configuration.DatabaseConnectionString);
            using var services = new ServiceCollection()
                .AddLogging(logging => logging.AddSimpleConsole())
                .AddDatabaseMaintenance(settings)
                .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            services.GetRequiredService<IDatabaseMaintenance>().Migrate();
            Console.WriteLine("Database maintenance completed. The service can be started.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Database maintenance failed: {exception.Message}");
            return 1;
        }
    }
}
