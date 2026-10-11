using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using EPaperDashboard.Services.Plugins;
using Microsoft.AspNetCore.DataProtection;

namespace EPaperDashboard.UnitTests.TestSupport;

public sealed class InMemoryProviderConnectionRepository : IProviderConnectionRepository
{
    private readonly List<ProviderConnection> connections = [];

    public static ProviderConnectionStore CreateStore() => new(new InMemoryProviderConnectionRepository(), new EphemeralDataProtectionProvider());

    public List<ProviderConnection> FindByUserId(UserId userId) => connections.Where(c => c.UserId == userId).ToList();
    public ProviderConnection? Find(string id, UserId userId) => connections.FirstOrDefault(c => c.Id == id && c.UserId == userId);
    public void Insert(ProviderConnection connection) => connections.Add(connection);
    public bool Update(ProviderConnection connection, string expectedCredentials) => Find(connection.Id, connection.UserId) is not null;
    public void Delete(string id, UserId userId) => connections.RemoveAll(c => c.Id == id && c.UserId == userId);
    public void DeleteByUserId(UserId userId) => connections.RemoveAll(c => c.UserId == userId);
}
