using EPaperDashboard.Utilities;

namespace EPaperDashboard.Services.Providers;

/// <summary>
/// Provides weather forecast data for the weather-forecast widget.
/// </summary>
public interface IWeatherForecastProvider
{
    Task<Result<List<WeatherForecast>>> FetchWeatherForecastAsync(string dashboardId, string entityId, string forecastType = "daily", CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers all available weather entities and fetches forecasts for each.
    /// </summary>
    Task<Result<Dictionary<string, List<WeatherForecast>>>> FetchAllWeatherForecastsAsync(string dashboardId, string forecastType = "daily", CancellationToken cancellationToken = default);
}
