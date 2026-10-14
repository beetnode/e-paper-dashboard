using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Services;
using EPaperDashboard.Utilities;

namespace EPaperDashboard.Data;

/// <summary>
/// Account recovery for whoever can run commands in the service's container, for when no administrator can sign in.
/// </summary>
internal static class UserAdminCommand
{
    private const string Usage = """
        Usage:
          --users promote <username>         Make the account an active administrator
          --users reset-password <username>  Print a temporary password that must be changed at sign-in
        """;

    internal static int Run(IEnvironmentConfiguration configuration, string[] args)
    {
        try
        {
            var settings = DatabaseSettings.Parse(configuration.DatabaseProvider, configuration.DatabaseConnectionString);
            using var services = new ServiceCollection()
                .AddSingleton(configuration)
                .AddPersistence(settings)
                .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            services.GetRequiredService<IPersistenceInitializer>().Initialize();
            var users = new UserService(
                services.GetRequiredService<IUserRepository>(),
                services.GetRequiredService<IDashboardRepository>(),
                unitOfWork: services.GetRequiredService<IUnitOfWork>());
            return Execute(args, users, Console.Out, Console.Error);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"User administration failed: {exception.Message}");
            return 1;
        }
    }

    internal static int Execute(string[] args, UserService users, TextWriter output, TextWriter error)
    {
        if (args is not ["--users", var action, var username] || action is not ("promote" or "reset-password"))
        {
            error.WriteLine(Usage);
            return 1;
        }

        if (action == "promote")
        {
            if (!users.TryPromoteFromConsole(username))
            {
                error.WriteLine($"No account named '{username}' exists.");
                return 1;
            }

            output.WriteLine($"'{username}' is now an active administrator.");
            return 0;
        }

        var temporaryPassword = users.TryResetPasswordFromConsole(username);
        if (temporaryPassword is null)
        {
            error.WriteLine($"No account named '{username}' exists.");
            return 1;
        }

        output.WriteLine($"Temporary password for '{username}': {temporaryPassword}");
        return 0;
    }
}
