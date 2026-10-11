using System.Text;
namespace EPaperDashboard.Services.Plugins;

/// <summary>Opaque, URL-safe source keys. Bare entity IDs remain legacy dashboard HA references.</summary>
public static class SourceReference
{
    private const string Prefix = "source:";
    public static string Create(string connectionId, string sourceId) =>
        $"{Prefix}{connectionId}:{Convert.ToBase64String(Encoding.UTF8.GetBytes(sourceId)).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";
    public static string ConnectionKey(string value) => TryParse(value, out var connectionId, out _) ? connectionId : "";
    public static bool IsQualified(string value) => value.StartsWith(Prefix, StringComparison.Ordinal);
    public static bool TryParse(string value, out string connectionId, out string sourceId)
    {
        connectionId = sourceId = "";
        if (!IsQualified(value)) return false;
        var parts = value.Split(':');
        if (parts.Length != 3 || !Guid.TryParseExact(parts[1], "N", out _) || parts[2].Length > 4096) return false;
        try
        {
            var encoded = parts[2].Replace('-', '+').Replace('_', '/');
            sourceId = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '=')));
            connectionId = parts[1];
            return sourceId.Length > 0 && Create(connectionId, sourceId) == value;
        }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException) { return false; }
    }
}
