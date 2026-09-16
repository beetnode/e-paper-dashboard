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
