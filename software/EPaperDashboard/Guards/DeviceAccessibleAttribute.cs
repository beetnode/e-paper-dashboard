namespace EPaperDashboard.Guards;

/// <summary>
/// Marks an endpoint as served only on the device port (e.g. 8129). It returns 404 on the web
/// port, and endpoints without this attribute return 404 on the device port.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class DeviceAccessibleAttribute : Attribute;
