using EPaperDashboard.Utilities;
using EPaperDashboard.Models;

namespace EPaperDashboard.Data.Repositories;

public interface IDeviceRepository
{
    Device? FindById(DeviceId id);
    Device? FindByIdentifier(string deviceIdentifier);
    Device? FindByApiKeyHash(string apiKeyHash);
    List<Device> FindByDashboardId(DashboardId dashboardId);
    List<Device> FindByUserId(UserId userId);
    void Insert(Device device);
    void Update(Device device);
    void Delete(DeviceId id);
    void DeleteByUserId(UserId userId);
}
