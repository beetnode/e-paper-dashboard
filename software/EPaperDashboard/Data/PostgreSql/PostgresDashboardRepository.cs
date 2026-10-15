using CSharpFunctionalExtensions;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;

namespace EPaperDashboard.Data.PostgreSql;

internal sealed class PostgresDashboardRepository(PostgresDbContext context) : IDashboardRepository
{
    public Dashboard? FindById(DashboardId id) =>
        context.FindOne<Dashboard>("SELECT document FROM izboard_dashboards WHERE id = @id",
            PostgresDbContext.Parameter("id", id.Value));

    public List<Dashboard> FindByUserId(UserId userId) => context.FindMany<Dashboard>(
        "SELECT document FROM izboard_dashboards WHERE user_id = @user_id",
        PostgresDbContext.Parameter("user_id", userId.Value));

    public IEnumerable<Dashboard> GetAll() => context.FindMany<Dashboard>(
        "SELECT document FROM izboard_dashboards");

    public void Insert(Dashboard dashboard)
    {
        if (dashboard.Id == DashboardId.Empty)
            dashboard.Id = DashboardId.New();

        context.Execute("""
            INSERT INTO izboard_dashboards (id, user_id, document)
            VALUES (@id, @user_id, @document)
            """,
            PostgresDbContext.Parameter("id", dashboard.Id.Value),
            PostgresDbContext.Parameter("user_id", dashboard.UserId.Value),
            PostgresDbContext.DocumentParameter(dashboard));
    }

    public void Update(Dashboard dashboard) => context.Execute("""
        UPDATE izboard_dashboards SET user_id = @user_id, document = @document WHERE id = @id
        """,
        PostgresDbContext.Parameter("id", dashboard.Id.Value),
        PostgresDbContext.Parameter("user_id", dashboard.UserId.Value),
        PostgresDbContext.DocumentParameter(dashboard));

    public void Delete(DashboardId id) => context.Execute(
        "DELETE FROM izboard_dashboards WHERE id = @id", PostgresDbContext.Parameter("id", id.Value));

    public void DeleteByUserId(UserId userId) => context.Execute(
        "DELETE FROM izboard_dashboards WHERE user_id = @user_id",
        PostgresDbContext.Parameter("user_id", userId.Value));
}
