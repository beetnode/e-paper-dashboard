using CSharpFunctionalExtensions;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;

namespace EPaperDashboard.Services;

public sealed class DashboardService(IDashboardRepository dashboardRepository)
{
    public List<Dashboard> GetDashboardsForUser(UserId userId) =>
        dashboardRepository.FindByUserId(userId);

    public void AddDashboard(Dashboard dashboard) =>
        dashboardRepository.Insert(dashboard);

    public void UpdateDashboard(Dashboard dashboard) =>
        dashboardRepository.Update(dashboard);

    public void DeleteDashboard(DashboardId dashboardId) =>
        dashboardRepository.Delete(dashboardId);

    public Dashboard? GetDashboardById(DashboardId dashboardId) =>
        dashboardRepository.FindById(dashboardId);

    public IEnumerable<Dashboard> GetAllDashboards() =>
        dashboardRepository.GetAll();
}
