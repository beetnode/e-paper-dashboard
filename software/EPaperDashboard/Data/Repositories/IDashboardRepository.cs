using EPaperDashboard.Utilities;
using EPaperDashboard.Models;

namespace EPaperDashboard.Data.Repositories;

public interface IDashboardRepository
{
    Dashboard? FindById(DashboardId id);
    List<Dashboard> FindByUserId(UserId userId);
    IEnumerable<Dashboard> GetAll();
    void Insert(Dashboard dashboard);
    void Update(Dashboard dashboard);
    void Delete(DashboardId id);
    void DeleteByUserId(UserId userId);
}
