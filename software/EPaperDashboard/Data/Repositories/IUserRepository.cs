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
    /// <summary>Whether an unsuspended super user other than <paramref name="except"/> exists.</summary>
    bool ExistsOtherActiveSuperUser(UserId except);
    bool IsSetupComplete();
    bool TryCreateAdministrator(User user);
    /// <summary>Stores <paramref name="candidate"/> as the setup code unless one exists, and returns the stored code.</summary>
    string GetOrCreateSetupCode(string candidate);
    bool? GetRenderingEnabled(UserId id);
    void SetRenderingEnabled(UserId id, bool enabled);
    AiConfig? GetAiConfig(UserId id);
    void SetAiConfig(UserId id, AiConfig config);
    bool TryInsert(User user);
    void Insert(User user);
    void Update(User user);
    void Delete(UserId id);
}
