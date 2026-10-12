using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using EPaperDashboard.Models;
using EPaperDashboard.Data.Repositories;
namespace EPaperDashboard.Services.Plugins;

/// <summary>
/// A connection supplied by the deployment instead of the user, such as the Home Assistant add-on's own server.
/// It is never stored, has no stored credentials, and cannot be changed or removed.
/// </summary>
public interface IDefaultConnectionSource
{
    /// <summary>The default connection of <paramref name="owner"/>; bare (unqualified) source IDs belong to it.</summary>
    ProviderConnection? DefaultConnection(UserId owner);
}

public sealed class ProviderConnectionStore(IProviderConnectionRepository repository, IDataProtectionProvider protection,
    IDefaultConnectionSource? defaults = null)
{
    private readonly IDataProtector protector = protection.CreateProtector("izboard.provider-credentials.v1");
    public string CacheVersion(UserId owner) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("|", repository.FindByUserId(owner).OrderBy(c => c.Id).Select(c => c.Id + ":" + c.ProtectedCredentials)))));
    public ProviderConnection? DefaultConnection(UserId owner) => defaults?.DefaultConnection(owner);
    public bool IsDefault(ProviderConnection connection) => DefaultConnection(connection.UserId)?.Id == connection.Id;
    public List<ProviderConnection> List(UserId owner) =>
        DefaultConnection(owner) is { } defaultConnection ? [defaultConnection, .. repository.FindByUserId(owner)] : repository.FindByUserId(owner);
    public ProviderConnection? Find(string id, UserId owner) =>
        DefaultConnection(owner) is { } defaultConnection && defaultConnection.Id == id ? defaultConnection : repository.Find(id, owner);
    public Dictionary<string, string> Credentials(ProviderConnection connection) => IsDefault(connection) ? [] :
        JsonSerializer.Deserialize<Dictionary<string, string>>(protector.Unprotect(connection.ProtectedCredentials)) ?? [];
    public void Create(ProviderConnection connection, Dictionary<string, string> credentials)
    {
        connection.ProtectedCredentials = protector.Protect(JsonSerializer.Serialize(credentials));
        repository.Insert(connection);
    }
    public bool Update(ProviderConnection connection, Dictionary<string, string> credentials)
    {
        if (IsDefault(connection)) return false;
        var expectedCredentials = connection.ProtectedCredentials;
        connection.ProtectedCredentials = protector.Protect(JsonSerializer.Serialize(credentials));
        return repository.Update(connection, expectedCredentials);
    }
    public void Delete(string id, UserId owner)
    {
        if (DefaultConnection(owner)?.Id == id) return;
        repository.Delete(id, owner);
    }
}
