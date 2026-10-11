using EPaperDashboard.Models;
namespace EPaperDashboard.Data.Repositories;

public interface IProviderConnectionRepository
{
    List<ProviderConnection> FindByUserId(UserId userId);
    ProviderConnection? Find(string id, UserId userId);
    void Insert(ProviderConnection connection);
    bool Update(ProviderConnection connection, string expectedCredentials);
    void Delete(string id, UserId userId);
    void DeleteByUserId(UserId userId);
}
