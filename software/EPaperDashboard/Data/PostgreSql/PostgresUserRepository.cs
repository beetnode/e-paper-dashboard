using CSharpFunctionalExtensions;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;

namespace EPaperDashboard.Data.PostgreSql;

internal sealed class PostgresUserRepository(PostgresDbContext context) : IUserRepository
{
    public Maybe<User> FindById(UserId id) => Maybe.From(
        context.FindOne<User>("SELECT document FROM izboard_users WHERE id = @id", PostgresDbContext.Parameter("id", id.Value)));

    public Maybe<User> FindByUsername(string username) => Maybe.From(
        context.FindOne<User>("SELECT document FROM izboard_users WHERE username = @username",
            PostgresDbContext.Parameter("username", username)));

    public List<User> GetAll() => context.FindMany<User>("SELECT document FROM izboard_users");

    public bool ExistsByUsername(string username) => context.Exists(
        "SELECT EXISTS (SELECT 1 FROM izboard_users WHERE username = @username)",
        PostgresDbContext.Parameter("username", username));

    public bool ExistsSuperUser() => context.Exists(
        "SELECT EXISTS (SELECT 1 FROM izboard_users WHERE is_super_user)");

    public bool ExistsOtherActiveSuperUser(UserId except) => context.Exists("""
        SELECT EXISTS (SELECT 1 FROM izboard_users WHERE is_super_user AND id <> @id
            AND NOT coalesce((document->>'IsSuspended')::boolean, false))
        """, PostgresDbContext.Parameter("id", except.Value));

    public bool IsSetupComplete() => context.Exists(
        "SELECT EXISTS (SELECT 1 FROM izboard_service_state WHERE id = 'setup') OR EXISTS (SELECT 1 FROM izboard_users)");

    public bool TryCreateAdministrator(User user) => context.ExecuteInTransaction(() =>
    {
        // This lock is shared by replicas and also excludes concurrent inserts/deletes.
        context.Execute("LOCK TABLE izboard_users IN EXCLUSIVE MODE");
        if (IsSetupComplete()) return false;
        user.IsSuperUser = true;
        Insert(user);
        context.Execute("INSERT INTO izboard_service_state (id) VALUES ('setup')");
        context.Execute("DELETE FROM izboard_service_state WHERE id = 'setup-code'");
        return true;
    });

    public string GetOrCreateSetupCode(string candidate)
    {
        context.Execute(
            "INSERT INTO izboard_service_state (id, document) VALUES ('setup-code', @document) ON CONFLICT (id) DO NOTHING",
            PostgresDbContext.DocumentParameter(new SetupCodeState(candidate)));
        return context.FindOne<SetupCodeState>(
            "SELECT document FROM izboard_service_state WHERE id = 'setup-code'")?.Code ?? candidate;
    }

    private sealed record SetupCodeState(string Code);

    public bool? GetRenderingEnabled(UserId id) => context.FindOne<RenderingPreference>(
        "SELECT jsonb_build_object('Enabled', enabled) FROM izboard_service_state WHERE id = @id",
        PostgresDbContext.Parameter("id", "rendering:" + id.Value))?.Enabled;

    public void SetRenderingEnabled(UserId id, bool enabled) => context.Execute(
        "INSERT INTO izboard_service_state (id, enabled) VALUES (@id, @enabled) ON CONFLICT (id) DO UPDATE SET enabled = EXCLUDED.enabled",
        PostgresDbContext.Parameter("id", "rendering:" + id.Value), PostgresDbContext.Parameter("enabled", enabled));

    private sealed record RenderingPreference(bool Enabled);

    public AiConfig? GetAiConfig(UserId id) => context.FindOne<AiConfig>(
        "SELECT document FROM izboard_service_state WHERE id = @id AND document IS NOT NULL",
        PostgresDbContext.Parameter("id", "ai:" + id.Value));

    public void SetAiConfig(UserId id, AiConfig config) => context.Execute(
        "INSERT INTO izboard_service_state (id, document) VALUES (@id, @document) ON CONFLICT (id) DO UPDATE SET document = EXCLUDED.document",
        PostgresDbContext.Parameter("id", "ai:" + id.Value), PostgresDbContext.DocumentParameter(config));

    public bool TryInsert(User user)
    {
        try { Insert(user); return true; }
        catch (Npgsql.PostgresException exception) when (exception.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }
    }

    public void Insert(User user)
    {
        if (user.Id == UserId.Empty)
            user.Id = UserId.New();

        context.Execute("""
            INSERT INTO izboard_users (id, username, is_super_user, document)
            VALUES (@id, @username, @is_super_user, @document)
            """,
            PostgresDbContext.Parameter("id", user.Id.Value),
            PostgresDbContext.Parameter("username", user.Username),
            PostgresDbContext.Parameter("is_super_user", user.IsSuperUser),
            PostgresDbContext.DocumentParameter(user));
    }

    public void Update(User user) => context.Execute("""
        UPDATE izboard_users SET username = @username, is_super_user = @is_super_user, document = @document
        WHERE id = @id
        """,
        PostgresDbContext.Parameter("id", user.Id.Value),
        PostgresDbContext.Parameter("username", user.Username),
        PostgresDbContext.Parameter("is_super_user", user.IsSuperUser),
        PostgresDbContext.DocumentParameter(user));

    public void Delete(UserId id) => context.Execute(
        "DELETE FROM izboard_users WHERE id = @id", PostgresDbContext.Parameter("id", id.Value));
}
