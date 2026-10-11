using EPaperDashboard.Models;
using EPaperDashboard.Data.Repositories;
namespace EPaperDashboard.Data.PostgreSql;

internal sealed class PostgresProviderConnectionRepository(PostgresDbContext context) : IProviderConnectionRepository
{
    public List<ProviderConnection> FindByUserId(UserId userId) => context.FindMany<ProviderConnection>(
        "SELECT document FROM izboard_provider_connections WHERE user_id = @user_id", PostgresDbContext.Parameter("user_id", userId.Value));
    public ProviderConnection? Find(string id, UserId userId) => context.FindOne<ProviderConnection>(
        "SELECT document FROM izboard_provider_connections WHERE id = @id AND user_id = @user_id",
        PostgresDbContext.Parameter("id", id), PostgresDbContext.Parameter("user_id", userId.Value));
    public void Insert(ProviderConnection connection) => context.Execute(
        "INSERT INTO izboard_provider_connections (id, user_id, document) VALUES (@id, @user_id, @document)",
        PostgresDbContext.Parameter("id", connection.Id), PostgresDbContext.Parameter("user_id", connection.UserId.Value), PostgresDbContext.DocumentParameter(connection));
    public bool Update(ProviderConnection connection, string expectedCredentials) => context.Execute(
        "UPDATE izboard_provider_connections SET document = @document WHERE id = @id AND user_id = @user_id AND document->>'ProtectedCredentials' = @expected",
        PostgresDbContext.Parameter("id", connection.Id), PostgresDbContext.Parameter("user_id", connection.UserId.Value), PostgresDbContext.DocumentParameter(connection), PostgresDbContext.Parameter("expected", expectedCredentials)) > 0;
    public void Delete(string id, UserId userId) => context.Execute(
        "DELETE FROM izboard_provider_connections WHERE id = @id AND user_id = @user_id",
        PostgresDbContext.Parameter("id", id), PostgresDbContext.Parameter("user_id", userId.Value));
    public void DeleteByUserId(UserId userId) => context.Execute(
        "DELETE FROM izboard_provider_connections WHERE user_id = @user_id", PostgresDbContext.Parameter("user_id", userId.Value));
}
