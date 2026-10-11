using CSharpFunctionalExtensions;
using EPaperDashboard.Models;

namespace EPaperDashboard.Data.Repositories;

public interface IUserRepository
{
    Maybe<User> FindById(UserId id);
    Maybe<User> FindByUsername(string username);
    List<User> GetAll();
    bool ExistsByUsername(string username);
    bool ExistsSuperUser();
    bool IsSetupComplete();
    bool TryCreateAdministrator(User user);
    bool? GetRenderingEnabled(UserId id);
    void SetRenderingEnabled(UserId id, bool enabled);
    bool TryInsert(User user);
    void Insert(User user);
    void Update(User user);
    void Delete(UserId id);
}
