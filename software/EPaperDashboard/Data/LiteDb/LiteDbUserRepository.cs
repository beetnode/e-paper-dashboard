using JsonSerializer = System.Text.Json.JsonSerializer;
using CSharpFunctionalExtensions;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using LiteDB;

namespace EPaperDashboard.Data.LiteDb;

internal sealed class LiteDbUserRepository(LiteDbContext context) : IUserRepository
{
    public Maybe<User> FindById(UserId id) =>
        context.Users.FindById(new ObjectId(id.Value));

    public Maybe<User> FindByUsername(string username) =>
        context.Users.FindOne(u => u.Username == username);

    public List<User> GetAll() =>
        [.. context.Users.FindAll()];

    public bool ExistsByUsername(string username) =>
        context.Users.Exists(u => u.Username == username);

    public bool ExistsByUsernameIgnoreCase(string username)
    {
        var lower = username.ToLowerInvariant();
        return context.Users.Exists(u => u.Username.ToLower() == lower);
    }

    public bool ExistsSuperUser() =>
        context.Users.Exists(u => u.IsSuperUser);

    public bool ExistsOtherActiveSuperUser(UserId except) =>
        context.Users.Exists(u => u.IsSuperUser && !u.IsSuspended && u.Id != except);

    public bool IsSetupComplete() => context.ServiceState.Exists(s => s["_id"] == "setup") || context.Users.Exists(_ => true);

    public bool TryCreateAdministrator(User user)
    {
        lock (context.UserWriteLock)
        {
            context.BeginTransaction();
            try
            {
                if (IsSetupComplete()) { context.Rollback(); return false; }
                user.IsSuperUser = true;
                Insert(user);
                context.ServiceState.Insert(new BsonDocument { ["_id"] = "setup", ["complete"] = true });
                context.ServiceState.Delete(SetupCodeId);
                context.Commit();
                return true;
            }
            catch { context.Rollback(); throw; }
        }
    }

    public string GetOrCreateSetupCode(string candidate)
    {
        lock (context.UserWriteLock)
        {
            var stored = context.ServiceState.FindById(SetupCodeId);
            if (stored is not null) return stored["code"].AsString;
            context.ServiceState.Insert(new BsonDocument { ["_id"] = SetupCodeId, ["code"] = candidate });
            return candidate;
        }
    }

    private const string SetupCodeId = "setup-code";

    public bool? GetRenderingEnabled(UserId id)
    {
        var state = context.ServiceState.FindById("rendering:" + id.Value);
        return state is null ? null : state["enabled"].AsBoolean;
    }

    public void SetRenderingEnabled(UserId id, bool enabled) =>
        context.ServiceState.Upsert(new BsonDocument { ["_id"] = "rendering:" + id.Value, ["enabled"] = enabled });

    public AiConfig? GetAiConfig(UserId id)
    {
        var state = context.ServiceState.FindById("ai:" + id.Value);
        return state is null ? null : JsonSerializer.Deserialize<AiConfig>(state["config"].AsString);
    }

    public void SetAiConfig(UserId id, AiConfig config) =>
        context.ServiceState.Upsert(new BsonDocument { ["_id"] = "ai:" + id.Value, ["config"] = JsonSerializer.Serialize(config) });

    public bool TryInsert(User user)
    {
        lock (context.UserWriteLock)
        {
            if (ExistsByUsername(user.Username)) return false;
            Insert(user);
            return true;
        }
    }

    public void Insert(User user)
    {
        if (user.Id == UserId.Empty)
            user.Id = UserId.New();
        context.Users.Insert(user);
    }

    public void Update(User user) =>
        context.Users.Update(user);

    public void Delete(UserId id) =>
        context.Users.Delete(new ObjectId(id.Value));
}
