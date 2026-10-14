using System.Text.Json;

namespace EPaperDashboard.Utilities;

public static class EnvironmentConfiguration
{
	private const string AppModeKey = "APP_MODE";
	private const string DatabaseProviderKey = "DATABASE_PROVIDER";
	private const string DatabaseConnectionStringKey = "DATABASE_CONNECTION_STRING";
	private const string ClientUrlKey = "CLIENT_URL";
	private const string DashboardScheduleCheckIntervalMinutesKey = "DASHBOARD_SCHEDULE_CHECK_INTERVAL_MINUTES";
	private const string DashboardMissedScheduleToleranceMinutesKey = "DASHBOARD_MISSED_SCHEDULE_TOLERANCE_MINUTES";
	private const string FirmwareUpdateEnabledKey = "FIRMWARE_UPDATE_ENABLED";
	private const string FirmwareReleaseProviderKey = "FIRMWARE_RELEASE_PROVIDER";
	private const string FirmwareGitHubRepoKey = "FIRMWARE_GITHUB_REPO";
	private const string FirmwareAssetPatternKey = "FIRMWARE_ASSET_PATTERN";
	private const string FirmwareCheckIntervalHoursKey = "FIRMWARE_CHECK_INTERVAL_HOURS";

	private static readonly Lazy<JsonDocument?> _jsonConfig = new(LoadJsonConfig);

	private static readonly Lazy<DeploymentMode> _appMode = new(() =>
	{
		var modeStr = GetStringFromEnvOrConfig(AppModeKey);
		if (!string.IsNullOrWhiteSpace(modeStr) &&
			Enum.TryParse<DeploymentMode>(modeStr, ignoreCase: true, out var mode))
		{
			return mode;
		}

		// Fallback: auto-detect addon mode for backward compatibility
		if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN"))
			&& Directory.Exists("/data"))
		{
			return DeploymentMode.Addon;
		}

		return DeploymentMode.Standalone;
	});

	private static readonly Lazy<Uri?> _clientUri = new(() =>
		GetUriFromEnvOrConfig(ClientUrlKey, UriKind.Absolute));

	private static readonly Lazy<TimeSpan> _dashboardScheduleCheckInterval = new(() =>
		TimeSpan.FromMinutes(GetIntFromEnvOrConfig(DashboardScheduleCheckIntervalMinutesKey, 720)));

	private static readonly Lazy<TimeSpan> _dashboardMissedScheduleTolerance = new(() =>
		TimeSpan.FromMinutes(GetIntFromEnvOrConfig(DashboardMissedScheduleToleranceMinutesKey, 15)));

	private static readonly Lazy<string> _configDir = new(() => "/data");
	private static readonly Lazy<string?> _databaseProvider = new(() => GetStringFromEnvOrConfig(DatabaseProviderKey));
	private static readonly Lazy<string?> _databaseConnectionString = new(() => GetStringFromEnvOrConfig(DatabaseConnectionStringKey));

	private static readonly Lazy<bool> _firmwareUpdateEnabled = new(() =>
	{
		var value = GetStringFromEnvOrConfig(FirmwareUpdateEnabledKey);
		return string.IsNullOrWhiteSpace(value) || !bool.TryParse(value, out var enabled) || enabled;
	});

	private static readonly Lazy<string> _firmwareReleaseProvider = new(() =>
		GetStringFromEnvOrConfig(FirmwareReleaseProviderKey) ?? "github");

	private static readonly Lazy<string> _firmwareGitHubRepo = new(() =>
		GetStringFromEnvOrConfig(FirmwareGitHubRepoKey) ?? "beetnode/izBoard");

	private static readonly Lazy<string> _firmwareAssetPattern = new(() =>
		GetStringFromEnvOrConfig(FirmwareAssetPatternKey) ?? "*.bin");

	private static readonly Lazy<TimeSpan> _firmwareCheckInterval = new(() =>
		TimeSpan.FromHours(GetIntFromEnvOrConfig(FirmwareCheckIntervalHoursKey, 6)));

	public static DeploymentMode AppMode => _appMode.Value;

	public static Uri? ClientUri => _clientUri.Value;



	public static TimeSpan DashboardScheduleCheckInterval => _dashboardScheduleCheckInterval.Value;

	public static TimeSpan DashboardMissedScheduleTolerance => _dashboardMissedScheduleTolerance.Value;

	public static string ConfigDir => _configDir.Value;
	public static string? DatabaseProvider => _databaseProvider.Value;
	public static string? DatabaseConnectionString => _databaseConnectionString.Value;

	public static string DataProtectionKeysDir => Path.Combine(ConfigDir, "DataProtection-Keys");

	public static bool FirmwareUpdateEnabled => _firmwareUpdateEnabled.Value;

	public static string FirmwareReleaseProvider => _firmwareReleaseProvider.Value;

	public static string FirmwareGitHubRepo => _firmwareGitHubRepo.Value;

	public static string FirmwareAssetPattern => _firmwareAssetPattern.Value;

	public static TimeSpan FirmwareCheckInterval => _firmwareCheckInterval.Value;

	private static JsonDocument? LoadJsonConfig()
	{
		try
		{
			var optionsFile = Path.Combine(ConfigDir, "options.json");
			if (!File.Exists(optionsFile))
			{
				return null;
			}

			var json = File.ReadAllText(optionsFile);
			return JsonDocument.Parse(json);
		}
		catch
		{
			return null;
		}
	}

	private static string? GetStringFromEnvOrConfig(string key)
	{
		var env = Environment.GetEnvironmentVariable(key);
		if (!string.IsNullOrWhiteSpace(env))
		{
			return env;
		}

		var doc = _jsonConfig.Value;
		if (doc is null)
		{
			return null;
		}

		if (!doc.RootElement.TryGetProperty(key, out var el))
		{
			return null;
		}

		return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
	}

	private static Uri? GetUriFromEnvOrConfig(string variable, UriKind kind)
	{
		var value = GetStringFromEnvOrConfig(variable);
		return !string.IsNullOrWhiteSpace(value) && Uri.TryCreate(value, kind, out var uri) ? uri : null;
	}

	private static int GetIntFromEnvOrConfig(string key, int defaultValue)
	{
		var stringValue = GetStringFromEnvOrConfig(key);
		return int.TryParse(stringValue, out var intValue) ? intValue : defaultValue;
	}
}
