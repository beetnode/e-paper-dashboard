namespace EPaperDashboard.Utilities;

/// <summary>
/// Injectable wrapper around the static <see cref="EnvironmentConfiguration"/> class.
/// Consumers should depend on <see cref="IEnvironmentConfiguration"/> rather than the static
/// class so they can be unit tested with a mocked configuration. The static class itself is kept
/// for the handful of reads in Program.cs that happen before the DI container is built.
/// </summary>
public sealed class EnvironmentConfigurationWrapper : IEnvironmentConfiguration
{
    public DeploymentMode AppMode => EnvironmentConfiguration.AppMode;
    public bool IsAddonMode => EnvironmentConfiguration.IsAddonMode;
    public Uri? ClientUri => EnvironmentConfiguration.ClientUri;
    public TimeSpan DashboardScheduleCheckInterval => EnvironmentConfiguration.DashboardScheduleCheckInterval;
    public TimeSpan DashboardMissedScheduleTolerance => EnvironmentConfiguration.DashboardMissedScheduleTolerance;
    public string ConfigDir => EnvironmentConfiguration.ConfigDir;
    public string? DatabaseProvider => EnvironmentConfiguration.DatabaseProvider;
    public string? DatabaseConnectionString => EnvironmentConfiguration.DatabaseConnectionString;
    public string DataProtectionKeysDir => EnvironmentConfiguration.DataProtectionKeysDir;
    public bool FirmwareUpdateEnabled => EnvironmentConfiguration.FirmwareUpdateEnabled;
    public string FirmwareReleaseProvider => EnvironmentConfiguration.FirmwareReleaseProvider;
    public string FirmwareGitHubRepo => EnvironmentConfiguration.FirmwareGitHubRepo;
    public string FirmwareAssetPattern => EnvironmentConfiguration.FirmwareAssetPattern;
    public TimeSpan FirmwareCheckInterval => EnvironmentConfiguration.FirmwareCheckInterval;
}
