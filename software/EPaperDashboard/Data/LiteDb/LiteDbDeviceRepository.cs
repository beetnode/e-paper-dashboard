using EPaperDashboard.Utilities;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using LiteDB;

namespace EPaperDashboard.Data.LiteDb;

internal sealed class LiteDbDeviceRepository(LiteDbContext context) : IDeviceRepository
{
    public Device? FindById(DeviceId id) =>
        context.Devices.FindById(new ObjectId(id.Value));

    public Device? FindByIdentifier(string deviceIdentifier) =>
        context.Devices.FindOne(d => d.DeviceIdentifier == deviceIdentifier);

    public Device? FindByApiKeyHash(string apiKeyHash) =>
        context.Devices.FindOne(d => d.ApiKeyHash == apiKeyHash);

    public List<Device> FindByDashboardId(DashboardId dashboardId) =>
        context.Devices.Find(d => d.DashboardId == dashboardId).ToList();

    public List<Device> FindByUserId(UserId userId) =>
        context.Devices.Find(d => d.UserId == userId).ToList();

    public void Insert(Device device)
    {
        if (device.Id == DeviceId.Empty)
            device.Id = DeviceId.New();
        context.Devices.Insert(device);
    }

    public void Update(Device device) =>
        context.Devices.Update(device);

    public void Delete(DeviceId id) =>
        context.Devices.Delete(new ObjectId(id.Value));

    public void DeleteByUserId(UserId userId) =>
        context.Devices.DeleteMany(d => d.UserId == userId);
}
