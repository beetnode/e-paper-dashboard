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

    public bool ExistsSuperUser() =>
        context.Users.Exists(u => u.IsSuperUser);

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
                context.Commit();
                return true;
            }
            catch { context.Rollback(); throw; }
        }
    }

    public bool? GetRenderingEnabled(UserId id)
    {
        var state = context.ServiceState.FindById("rendering:" + id.Value);
        return state is null ? null : state["enabled"].AsBoolean;
    }

    public void SetRenderingEnabled(UserId id, bool enabled) =>
        context.ServiceState.Upsert(new BsonDocument { ["_id"] = "rendering:" + id.Value, ["enabled"] = enabled });

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
