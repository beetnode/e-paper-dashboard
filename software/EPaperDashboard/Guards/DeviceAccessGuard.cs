using Microsoft.AspNetCore.Http;

namespace EPaperDashboard.Guards;

/// <summary>
/// Keeps device endpoints and the web app apart, based on the <see cref="DeviceAccessibleAttribute"/>
/// metadata: device endpoints answer only on the TLS device port (e.g. 8129), everything else only on
/// the web port, so a device key is never accepted over the web port.
/// </summary>
public static class DeviceAccessGuard
{
    public static bool IsAccessible(Endpoint? endpoint) =>
        endpoint?.Metadata.GetMetadata<DeviceAccessibleAttribute>() is not null;

    public static bool IsServedOnPort(Endpoint? endpoint, bool isDevicePort) =>
        isDevicePort == IsAccessible(endpoint);
}
