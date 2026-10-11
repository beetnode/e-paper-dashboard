using System.Text.Json;
using CSharpFunctionalExtensions;

namespace EPaperDashboard.Services.Providers.HomeAssistant;

/// <summary>
/// Home Assistant implementation of <see cref="IEntityHistoryProvider"/>.
/// Fetches entity history via the Home Assistant REST API.
/// </summary>
public class HomeAssistantEntityHistoryProvider(
    HomeAssistantConnectionService connection,
    IHttpClientFactory httpClientFactory,
    ILogger<HomeAssistantEntityHistoryProvider> logger)
{
    private readonly HomeAssistantConnectionService _connection = connection;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<HomeAssistantEntityHistoryProvider> _logger = logger;

    public async Task<Result<Dictionary<string, List<HistoryState>>, string>> FetchEntityHistoryAsync(HomeAssistantScope scope, IEnumerable<string> entityIds, int hours = 24, CancellationToken cancellationToken = default)
    {
        var connectionInfo = _connection.GetConnectionInfo(scope);
        if (connectionInfo.IsFailure)
            return connectionInfo.Error;

        var requestedIds = new HashSet<string>(entityIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
        if (requestedIds.Count == 0)
            return Result.Success<Dictionary<string, List<HistoryState>>, string>(new Dictionary<string, List<HistoryState>>());

        var (hostUrl, token) = connectionInfo.Value;

        try
        {
            using var httpClient = _httpClientFactory.CreateClient(Utilities.Constants.HassHttpClientName);
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            var entityIdParams = string.Join("&", requestedIds.Select(id => $"filter_entity_id={Uri.EscapeDataString(id)}"));
            var startTime = DateTime.UtcNow.AddHours(-hours).ToString("O");
            var endTime = DateTime.UtcNow.ToString("O");

            var url = $"{hostUrl}/api/history/period/{startTime}?{entityIdParams}&end_time={endTime}";

            using var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("History API call failed with status {Status}: {Error}", response.StatusCode, errorContent);
                return $"Failed to fetch history: {response.StatusCode}";
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogDebug("HomeAssistant FetchEntityHistory raw response: {Response}", content);

            var historyData = JsonSerializer.Deserialize<JsonElement>(content);
            var result = new Dictionary<string, List<HistoryState>>();

            if (historyData.ValueKind == JsonValueKind.Array)
            {
                foreach (var entityHistory in historyData.EnumerateArray())
                {
                    if (entityHistory.ValueKind != JsonValueKind.Array || entityHistory.GetArrayLength() == 0)
                        continue;

                    var firstState = entityHistory[0];
                    if (!firstState.TryGetProperty("entity_id", out var entityIdProp))
                        continue;

                    var entityId = entityIdProp.GetString();
                    if (string.IsNullOrWhiteSpace(entityId))
                        continue;

                    var states = new List<HistoryState>();
                    foreach (var stateItem in entityHistory.EnumerateArray())
                    {
                        var historyState = ParseHistoryState(stateItem);
                        if (historyState != null)
                            states.Add(historyState);
                    }

                    result[entityId] = states;
                }
            }

            return Result.Success<Dictionary<string, List<HistoryState>>, string>(result);
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error while fetching entity history");
            return $"Failed to fetch entity history: {ex.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch entity history");
            return $"Failed to fetch entity history: {ex.Message}";
        }
    }

    private HistoryState? ParseHistoryState(JsonElement element)
    {
        try
        {
            if (element.ValueKind != JsonValueKind.Object)
                return null;

            var state = element.TryGetProperty("state", out var stateProp)
                ? stateProp.GetString() ?? string.Empty
                : string.Empty;

            var lastChangedStr = element.TryGetProperty("last_changed", out var lastChangedProp)
                ? lastChangedProp.GetString()
                : null;

            if (!DateTime.TryParse(lastChangedStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var lastChanged))
                lastChanged = DateTime.UtcNow;

            var attributes = new Dictionary<string, JsonElement>();
            if (element.TryGetProperty("attributes", out var attrsProp) && attrsProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var attr in attrsProp.EnumerateObject())
                    attributes[attr.Name] = attr.Value;
            }

            double numericValue = 0;
            if (!double.TryParse(state, System.Globalization.CultureInfo.InvariantCulture, out numericValue))
            {
                if (element.TryGetProperty("entity_id", out var entityIdProp))
                {
                    var entityId = entityIdProp.GetString() ?? string.Empty;
                    numericValue = ExtractNumericFromAttributes(entityId, attributes, state);
                }
            }

            return new HistoryState
            {
                State = state,
                NumericValue = numericValue,
                LastChanged = lastChanged,
                Attributes = attributes
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse history state element");
            return null;
        }
    }

    private static double ExtractNumericFromAttributes(string entityId, Dictionary<string, JsonElement> attributes, string state)
    {
        var domain = entityId.Split('.')[0];

        string[] candidateAttributes = domain switch
        {
            "climate" => new[] { "current_temperature", "temperature", "current_humidity", "humidity" },
            "weather" => new[] { "temperature", "humidity", "pressure", "wind_speed" },
            "light" => new[] { "brightness", "color_temp" },
            "cover" => new[] { "current_position", "current_tilt_position" },
            "fan" => new[] { "percentage", "speed" },
            "humidifier" => new[] { "current_humidity", "humidity" },
            "water_heater" => new[] { "current_temperature", "temperature" },
            "sun" => new[] { "elevation", "azimuth" },
            "device_tracker" or "person" => new[] { "latitude", "longitude", "gps_accuracy" },
            "zone" => new[] { "latitude", "longitude", "radius" },
            _ => Array.Empty<string>()
        };

        foreach (var attrName in candidateAttributes)
        {
            if (attributes.TryGetValue(attrName, out var attrValue))
            {
                if (attrValue.ValueKind == JsonValueKind.Number && attrValue.TryGetDouble(out var doubleVal))
                    return doubleVal;
                else if (attrValue.ValueKind == JsonValueKind.String)
                {
                    var strVal = attrValue.GetString();
                    if (double.TryParse(strVal, System.Globalization.CultureInfo.InvariantCulture, out var parsedVal))
                        return parsedVal;
                }
            }
        }

        if (domain == "binary_sensor")
        {
            return state.ToLowerInvariant() switch
            {
                "on" => 1.0,
                "off" => 0.0,
                "true" => 1.0,
                "false" => 0.0,
                _ => 0.0
            };
        }

        return 0;
    }
}
