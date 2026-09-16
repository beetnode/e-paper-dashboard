namespace EPaperDashboard.Utilities;

public interface IEnvironmentConfiguration
{
    DeploymentMode AppMode { get; }
    bool IsAddonMode { get; }
    string? HomeAssistantHost { get; }
    Uri? ClientUri { get; }
    string? SuperUserUsername { get; }
    string? SuperUserPassword { get; }
    string? StateSigningKey { get; }
    TimeSpan DashboardScheduleCheckInterval { get; }
    TimeSpan DashboardMissedScheduleTolerance { get; }
    string ConfigDir { get; }
    string? DatabaseProvider { get; }
    string? DatabaseConnectionString { get; }
    string DataProtectionKeysDir { get; }
    bool FirmwareUpdateEnabled { get; }
    string FirmwareReleaseProvider { get; }
    string FirmwareGitHubRepo { get; }
    string FirmwareAssetPattern { get; }
    TimeSpan FirmwareCheckInterval { get; }
}
