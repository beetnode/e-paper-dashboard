using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using EPaperDashboard.Models;
using EPaperDashboard.Data.Repositories;
namespace EPaperDashboard.Services.Plugins;

public sealed class ProviderConnectionStore(IProviderConnectionRepository repository, IDataProtectionProvider protection)
{
    private readonly IDataProtector protector = protection.CreateProtector("izboard.provider-credentials.v1");
    public string CacheVersion(UserId owner) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("|", repository.FindByUserId(owner).OrderBy(c => c.Id).Select(c => c.Id + ":" + c.ProtectedCredentials)))));
    public List<ProviderConnection> List(UserId owner) => repository.FindByUserId(owner);
    public ProviderConnection? Find(string id, UserId owner) => repository.Find(id, owner);
    public Dictionary<string, string> Credentials(ProviderConnection connection) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(protector.Unprotect(connection.ProtectedCredentials)) ?? [];
    public void Create(ProviderConnection connection, Dictionary<string, string> credentials)
    {
        connection.ProtectedCredentials = protector.Protect(JsonSerializer.Serialize(credentials));
        repository.Insert(connection);
    }
    public bool Update(ProviderConnection connection, Dictionary<string, string> credentials)
    {
        var expectedCredentials = connection.ProtectedCredentials;
        connection.ProtectedCredentials = protector.Protect(JsonSerializer.Serialize(credentials));
        return repository.Update(connection, expectedCredentials);
    }
    public void Delete(string id, UserId owner) => repository.Delete(id, owner);
}
