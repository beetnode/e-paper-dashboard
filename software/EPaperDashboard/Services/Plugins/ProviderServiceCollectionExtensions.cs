using EPaperDashboard.Services.Plugins.Google;
using EPaperDashboard.Services.Plugins.HomeAssistant;
using EPaperDashboard.Services.Providers;
namespace EPaperDashboard.Services.Plugins;

public static class ProviderServiceCollectionExtensions
{
    public static IServiceCollection AddDataProviderPlugins(this IServiceCollection services)
    {
        services.AddSingleton<ProviderConnectionStore>();
        services.AddSingleton<ProviderRegistry>();
        services.AddSingleton<PluginDataRouter>();
        services.AddSingleton<IEntityStateProvider>(sp => sp.GetRequiredService<PluginDataRouter>());
        services.AddSingleton<ICalendarDataProvider>(sp => sp.GetRequiredService<PluginDataRouter>());
        services.AddSingleton<ITodoDataProvider>(sp => sp.GetRequiredService<PluginDataRouter>());
        services.AddSingleton<IWeatherForecastProvider>(sp => sp.GetRequiredService<PluginDataRouter>());
        services.AddSingleton<IEntityHistoryProvider>(sp => sp.GetRequiredService<PluginDataRouter>());
        services.AddSingleton<IRssFeedDataProvider>(sp => sp.GetRequiredService<PluginDataRouter>());
        services.AddHomeAssistantPlugin();
        services.AddGooglePlugin();
        return services;
    }

}
