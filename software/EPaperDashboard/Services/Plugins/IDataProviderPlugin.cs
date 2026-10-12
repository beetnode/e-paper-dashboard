using CSharpFunctionalExtensions;
using System.Text.Json;
using EPaperDashboard.Models.Rendering;
using EPaperDashboard.Models;
namespace EPaperDashboard.Services.Plugins;

public static class SourceCapabilities
{
    public const string Weather = "weather", State = "state", Calendar = "calendar", Todo = "todo", Forecast = "forecast", History = "history", Feed = "feed";
}

public sealed record CredentialField(string Key, string Label, bool Secret = false);
public sealed record ProviderManifest(string Id, string Name, IReadOnlyList<CredentialField> Credentials, bool RequiresOAuth = false, IReadOnlyList<ProviderWidget>? Widgets = null, string? SetupInstructions = null);
public sealed record ProviderWidget(string Type, string? RenderAs = null);
/// <summary>The connection has been checked to belong to the dashboard owner.</summary>
public sealed record ProviderContext(string DashboardId, ProviderConnection Connection);
public sealed record SourceQuery(string Capability, string SourceId, int Hours = 168, string ForecastType = "daily", JsonElement? Parameters = null);
public sealed record PluginWidgetRequest(string Source, string Capability, JsonElement? Parameters = null);

/// <summary>Stable metadata shared by every plugin; capabilities determine widget compatibility.</summary>
public sealed record DataSource
{
    public string EntityId { get; init; } = "";
    public string FriendlyName { get; init; } = "";
    public string Domain { get; init; } = "";
    public string? DeviceClass { get; init; }
    public string? UnitOfMeasurement { get; init; }
    public string? Icon { get; init; }
    public string? State { get; init; }
    public int? SupportedFeatures { get; init; }
    public string[] Capabilities { get; init; } = [];
    public string PluginId { get; init; } = "";
    public string? ConnectionId { get; init; }
    public string? ConnectionName { get; init; }
}

/// <summary>Normalized results: plugins translate their wire formats at this boundary.</summary>
public sealed record SourceData
{
    public JsonElement? Custom { get; init; }
    public EntityState? State { get; init; }
    public List<CalendarEvent> CalendarEvents { get; init; } = [];
    public List<TodoItem> TodoItems { get; init; } = [];
    public List<WeatherForecast> Forecast { get; init; } = [];
    public List<HistoryState> History { get; init; } = [];
    public List<RssFeedEntry> Feed { get; init; } = [];
}

/// <summary>Built-in plugins register via DI. UI modules register widgets in the frontend plugin catalog.</summary>
public interface IDataProviderPlugin
{
    ProviderManifest Manifest { get; }
    // A bespoke widget may request an arbitrary plugin capability in addition to common data.
    PluginWidgetRequest? GetWidgetDataRequest(WidgetConfigEntry widget) => null;
    string? ValidateCredentials(IReadOnlyDictionary<string, string> credentials);
    Task<Result<List<DataSource>, string>> DiscoverAsync(ProviderContext context, CancellationToken cancellationToken);
    Task<Result<SourceData, string>> FetchAsync(ProviderContext context, SourceQuery query, CancellationToken cancellationToken);

    async Task<Result<Dictionary<string, SourceData>, string>> FetchManyAsync(ProviderContext context, string capability, string[] sourceIds, int hours, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, SourceData>();
        foreach (var sourceId in sourceIds)
        {
            var result = await FetchAsync(context, new(capability, sourceId, hours), cancellationToken);
            if (result.IsFailure) return result.Error;
            values[sourceId] = result.Value;
        }
        return values;
    }
}

/// <summary>
/// Plugins whose connections can be authorized through a browser redirect. The core exposes a generic
/// authorize endpoint and an anonymous <c>/api/providers/{id}/callback</c> endpoint for them.
/// Sign-in is mandatory only when the manifest sets <see cref="ProviderManifest.RequiresOAuth"/>.
/// </summary>
public interface IOAuthDataProviderPlugin : IDataProviderPlugin
{
    /// <summary>
    /// Credentials produced by authorization. When other credentials change they are discarded,
    /// unless the same update supplies them explicitly.
    /// </summary>
    IReadOnlyCollection<string> AuthorizationCredentialKeys { get; }
    bool IsAuthorized(IReadOnlyDictionary<string, string> credentials);
    /// <summary>Returns the provider sign-in URL and a one-time state value that the core binds to the browser.</summary>
    Result<(string Url, string State), string> StartAuthorization(ProviderConnection connection, HttpContext httpContext);
    Task<UnitResult<string>> CompleteAuthorizationAsync(string? state, string? browserState, string? code, CancellationToken cancellationToken);
}

public sealed class ProviderRegistry
{
    private readonly Dictionary<string, IDataProviderPlugin> plugins;
    private readonly Dictionary<string, (IDataProviderPlugin Owner, ProviderWidget Widget)> widgets = new(StringComparer.Ordinal);

    public ProviderRegistry(IEnumerable<IDataProviderPlugin> plugins)
    {
        this.plugins = plugins.ToDictionary(p => p.Manifest.Id, StringComparer.Ordinal);
        foreach (var plugin in this.plugins.Values)
        {
            if (plugin.Manifest.RequiresOAuth && plugin is not IOAuthDataProviderPlugin)
                throw new InvalidOperationException($"Plugin {plugin.Manifest.Id} requires OAuth and must implement {nameof(IOAuthDataProviderPlugin)}.");
            foreach (var widget in plugin.Manifest.Widgets ?? [])
            {
                if (!widget.Type.StartsWith(plugin.Manifest.Id + ".", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Widget {widget.Type} must be namespaced with {plugin.Manifest.Id}.");
                widgets.Add(widget.Type, (plugin, widget));
            }
        }
    }

    public IEnumerable<IDataProviderPlugin> Plugins => plugins.Values;
    public IEnumerable<ProviderManifest> Manifests => plugins.Values.Select(p => p.Manifest);
    public IEnumerable<ProviderWidget> Widgets => widgets.Values.Select(value => value.Widget);
    public IDataProviderPlugin? WidgetOwner(string type) => widgets.TryGetValue(type, out var value) ? value.Owner : null;
    public string ResolveWidgetType(string type) => widgets.TryGetValue(type, out var value) ? value.Widget.RenderAs ?? type : type;
    public IDataProviderPlugin? Find(string id) => plugins.GetValueOrDefault(id);
}
