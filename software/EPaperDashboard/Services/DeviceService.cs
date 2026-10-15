using EPaperDashboard.Utilities;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;

namespace EPaperDashboard.Services;

public sealed class DeviceService(IDeviceRepository deviceRepository, IUserRepository? users = null)
{
    public List<Device> GetDevicesForDashboard(DashboardId dashboardId) =>
        deviceRepository.FindByDashboardId(dashboardId);

    public List<Device> GetDevicesForUser(UserId userId) =>
        deviceRepository.FindByUserId(userId);

    public void AddDevice(Device device) =>
        deviceRepository.Insert(device);

    public void UpdateDevice(Device device) =>
        deviceRepository.Update(device);

    public void DeleteDevice(DeviceId deviceId) =>
        deviceRepository.Delete(deviceId);

    public Device? GetDeviceById(DeviceId deviceId) =>
        deviceRepository.FindById(deviceId);

    public Device? GetDeviceByIdentifier(string deviceIdentifier) =>
        deviceRepository.FindByIdentifier(deviceIdentifier);

    /// <summary>Finds the device a key belongs to; keys of devices whose owner is suspended stop working.</summary>
    public Device? GetDeviceByApiKey(string apiKey) =>
        deviceRepository.FindByApiKey(apiKey) is { } device && users?.FindById(device.UserId)?.IsSuspended != true
            ? device
            : null;
}
