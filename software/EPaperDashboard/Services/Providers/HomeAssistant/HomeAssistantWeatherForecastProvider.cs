using System.Net.WebSockets;
using System.Text.Json;
using CSharpFunctionalExtensions;

namespace EPaperDashboard.Services.Providers.HomeAssistant;

/// <summary>
/// Home Assistant implementation of <see cref="IWeatherForecastProvider"/>.
/// Fetches weather forecasts via the Home Assistant WebSocket API.
/// </summary>
public class HomeAssistantWeatherForecastProvider(
    HomeAssistantConnectionService connection,
    ILogger<HomeAssistantWeatherForecastProvider> logger)
{
    private readonly HomeAssistantConnectionService _connection = connection;
    private readonly ILogger<HomeAssistantWeatherForecastProvider> _logger = logger;

    public async Task<Result<List<WeatherForecast>, string>> FetchWeatherForecastAsync(HomeAssistantScope scope, string weatherEntityId, string forecastType = "daily", CancellationToken cancellationToken = default)
    {
        var connectionInfo = _connection.GetConnectionInfo(scope);
        if (connectionInfo.IsFailure)
            return connectionInfo.Error;

        if (string.IsNullOrWhiteSpace(weatherEntityId))
            return "Weather entity ID is required";

        var (hostUrl, token) = connectionInfo.Value;

        try
        {
            using var ws = await WebSocketHelpers.ConnectAndAuthenticateAsync(hostUrl, token, connectionInfo.Value.WebSocketPath, cancellationToken);

            var messageId = _connection.NextMessageId();

            await HomeAssistantConnectionService.SendMessageAsync(ws, new
            {
                id = messageId,
                type = "call_service",
                domain = "weather",
                service = "get_forecasts",
                service_data = new
                {
                    type = forecastType
                },
                target = new
                {
                    entity_id = weatherEntityId
                },
                return_response = true
            }, cancellationToken);

            var response = await HomeAssistantConnectionService.ReceiveMessageAsync(ws, cancellationToken);
            _logger.LogDebug("HomeAssistant FetchWeatherForecast raw response: {Response}", response);

            var json = JsonSerializer.Deserialize<JsonElement>(response);
            var forecastData = new List<WeatherForecast>();

            if (json.TryGetProperty("success", out var success) && success.GetBoolean() &&
                json.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object)
            {
                JsonElement forecastArray = default;
                bool foundForecast = false;

                if (result.TryGetProperty("response", out var responseObj) && responseObj.ValueKind == JsonValueKind.Object)
                {
                    if (responseObj.TryGetProperty(weatherEntityId, out var entityObj) &&
                        entityObj.ValueKind == JsonValueKind.Object &&
                        entityObj.TryGetProperty("forecast", out forecastArray) &&
                        forecastArray.ValueKind == JsonValueKind.Array)
                    {
                        foundForecast = true;
                        _logger.LogDebug("Found forecast at result.response.{EntityId}.forecast", weatherEntityId);
                    }
                }

                if (!foundForecast && result.TryGetProperty(weatherEntityId, out var entityObj2) &&
                    entityObj2.ValueKind == JsonValueKind.Object &&
                    entityObj2.TryGetProperty("forecast", out forecastArray) &&
                    forecastArray.ValueKind == JsonValueKind.Array)
                {
                    foundForecast = true;
                    _logger.LogDebug("Found forecast at result.{EntityId}.forecast", weatherEntityId);
                }

                if (!foundForecast && result.TryGetProperty("forecast", out forecastArray) &&
                    forecastArray.ValueKind == JsonValueKind.Array)
                {
                    foundForecast = true;
                    _logger.LogDebug("Found forecast at result.forecast");
                }

                if (foundForecast)
                {
                    foreach (var item in forecastArray.EnumerateArray())
                    {
                        forecastData.Add(ParseForecast(item));
                    }
                    _logger.LogDebug("Parsed {Count} forecast items from entity {EntityId}", forecastData.Count, weatherEntityId);

                    if (forecastData.Count > 0)
                    {
                        _logger.LogDebug("First forecast item datetime: {DateTime}", forecastData[0].Datetime ?? "NOT FOUND");
                    }
                }
                else
                {
                    _logger.LogWarning("Could not find forecast array in weather.get_forecasts response for entity {EntityId}. Response was: {Response}", weatherEntityId, response);
                }
            }
            else
            {
                _logger.LogWarning("Weather forecast fetch returned unsuccessful response. Response was: {Response}", response);
            }

            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None); } catch { /* using disposes socket */ }
            return forecastData;
        }
        catch (OperationCanceledException) { throw; }
        catch (WebSocketException)
        {
            return "Unable to connect to Home Assistant WebSocket. Please check the Host URL and ensure it's accessible.";
        }
        catch (Exception ex)
        {
            return $"Failed to fetch weather forecast: {ex.Message}";
        }
    }


    private static WeatherForecast ParseForecast(JsonElement item) => new()
    {
        Datetime = GetString(item, "datetime"),
        Condition = GetString(item, "condition"),
        Temperature = GetNumber(item, "temperature"),
        TempLow = GetNumber(item, "templow"),
        PrecipitationProbability = GetNumber(item, "precipitation_probability"),
        WindSpeed = GetNumber(item, "wind_speed")
    };

    private static string? GetString(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) ? value.ToString() : null;

    private static double? GetNumber(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        return double.TryParse(
            value.ToString(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out number)
                ? number
                : null;
    }

}
