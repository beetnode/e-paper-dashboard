namespace EPaperDashboard.Models;

/// <summary>A reusable connection owned by one user. Secrets never appear in API responses.</summary>
public sealed class ProviderConnection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public UserId UserId { get; set; }
    public string PluginId { get; set; } = "";
    public string Name { get; set; } = "";
    public string ProtectedCredentials { get; set; } = "";
}
