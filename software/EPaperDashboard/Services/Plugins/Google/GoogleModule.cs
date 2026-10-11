namespace EPaperDashboard.Services.Plugins.Google;

public static class GoogleModule
{
    public static IServiceCollection AddGooglePlugin(this IServiceCollection services)
    {
        services.AddHttpClient("provider-google", client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<GoogleOAuthService>();
        services.AddSingleton<IDataProviderPlugin, GooglePlugin>();
        return services;
    }
}
