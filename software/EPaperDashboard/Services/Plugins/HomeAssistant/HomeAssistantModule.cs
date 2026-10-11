using EPaperDashboard.Services.Providers.HomeAssistant;
namespace EPaperDashboard.Services.Plugins.HomeAssistant;

public static class HomeAssistantModule
{
    public static IServiceCollection AddHomeAssistantPlugin(this IServiceCollection services)
    {
        services.AddSingleton<HomeAssistantAuthService>();
        services.AddSingleton<HomeAssistantConnectionService>();
        services.AddSingleton<HomeAssistantService>();
        services.AddSingleton<HomeAssistantEntityStateProvider>();
        services.AddSingleton<HomeAssistantCalendarDataProvider>();
        services.AddSingleton<HomeAssistantTodoDataProvider>();
        services.AddSingleton<HomeAssistantWeatherForecastProvider>();
        services.AddSingleton<HomeAssistantEntityHistoryProvider>();
        services.AddSingleton<HomeAssistantRssFeedDataProvider>();
        services.AddSingleton<IDataProviderPlugin, HomeAssistantPlugin>();
        return services;
    }

}
