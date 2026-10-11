using EPaperDashboard.Models;
using EPaperDashboard.Data.Repositories;
namespace EPaperDashboard.Data.LiteDb;

internal sealed class LiteDbProviderConnectionRepository(LiteDbContext context) : IProviderConnectionRepository
{
    public List<ProviderConnection> FindByUserId(UserId userId) => context.ProviderConnections.Find(c => c.UserId == userId).ToList();
    public ProviderConnection? Find(string id, UserId userId) => context.ProviderConnections.FindOne(c => c.Id == id && c.UserId == userId);
    public void Insert(ProviderConnection connection) => context.ProviderConnections.Insert(connection);
    public bool Update(ProviderConnection connection, string expectedCredentials)
    {
        lock (context.UserWriteLock)
        {
            var saved = Find(connection.Id, connection.UserId);
            if (saved is null || saved.ProtectedCredentials != expectedCredentials) return false;
            return context.ProviderConnections.Update(connection);
        }
    }
    public void Delete(string id, UserId userId) => context.ProviderConnections.DeleteMany(c => c.Id == id && c.UserId == userId);
    public void DeleteByUserId(UserId userId) => context.ProviderConnections.DeleteMany(c => c.UserId == userId);
}
