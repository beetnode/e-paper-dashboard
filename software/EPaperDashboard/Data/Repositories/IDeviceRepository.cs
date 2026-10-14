using CSharpFunctionalExtensions;
using EPaperDashboard.Models;

namespace EPaperDashboard.Data.Repositories;

public interface IDeviceRepository
{
    Maybe<Device> FindById(DeviceId id);
    Maybe<Device> FindByIdentifier(string deviceIdentifier);
    Maybe<Device> FindByApiKey(string apiKey);
    List<Device> FindByDashboardId(DashboardId dashboardId);
    List<Device> FindByUserId(UserId userId);
    void Insert(Device device);
    void Update(Device device);
    void Delete(DeviceId id);
    void DeleteByUserId(UserId userId);
}
