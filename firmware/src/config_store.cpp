#include "config_store.h"
#include "constants.h"
#include <Preferences.h>

ConfigStore::ConfigStore(Logger& logger) : _logger(logger) {}

std::optional<DeviceConfig> ConfigStore::load() const
{
  Preferences prefs;
  prefs.begin(StorageKey::Namespace, true);
  DeviceConfig config{
      prefs.getString(StorageKey::Ssid, ""),
      prefs.getString(StorageKey::Password, ""),
      prefs.getString(StorageKey::DashboardUrl, ""),
      prefs.getInt(StorageKey::DevicePort, DeviceServer::DefaultDevicePort),
      prefs.getString(StorageKey::DashboardBasePath, ""),
      prefs.getString(StorageKey::DashboardApiKey, ""),
      prefs.getString(StorageKey::PairingCode, ""),
      prefs.getString(StorageKey::RegistrationToken, ""),
      prefs.getString(StorageKey::ServerFingerprint, "")};
  prefs.end();

  if (config.ssid.length() == 0 || config.dashboardUrl.length() == 0)
  {
    return std::nullopt;
  }
  return config;
}

void ConfigStore::save(const DeviceConfig& config) const
{
  Preferences prefs;
  prefs.begin(StorageKey::Namespace, false);
  prefs.putString(StorageKey::Ssid, config.ssid);
  prefs.putString(StorageKey::Password, config.password);
  prefs.putString(StorageKey::DashboardUrl, config.dashboardUrl);
  prefs.putInt(StorageKey::DevicePort, config.devicePort);
  prefs.putString(StorageKey::DashboardBasePath, config.dashboardBasePath);
  prefs.putString(StorageKey::DashboardApiKey, config.dashboardApiKey);
  prefs.putString(StorageKey::PairingCode, config.pairingCode);
  prefs.putString(StorageKey::RegistrationToken, config.registrationToken);
  prefs.putString(StorageKey::ServerFingerprint, config.serverFingerprint);
  prefs.end();
}

void ConfigStore::clear() const
{
  Preferences prefs;
  prefs.begin(StorageKey::Namespace, false);
  prefs.clear();
  prefs.end();
}

int ConfigStore::getOtaFailCount(const String& version) const
{
  Preferences prefs;
  prefs.begin(StorageKey::Namespace, true);
  String failedVersion = prefs.getString(StorageKey::OtaFailVersion, "");
  int count = 0;
  if (failedVersion == version)
  {
    count = prefs.getInt(StorageKey::OtaFailCount, 0);
  }
  prefs.end();
  return count;
}

void ConfigStore::recordOtaFailure(const String& version) const
{
  Preferences prefs;
  prefs.begin(StorageKey::Namespace, false);
  String failedVersion = prefs.getString(StorageKey::OtaFailVersion, "");
  int count = 0;
  if (failedVersion == version)
  {
    count = prefs.getInt(StorageKey::OtaFailCount, 0);
  }
  prefs.putString(StorageKey::OtaFailVersion, version);
  prefs.putInt(StorageKey::OtaFailCount, count + 1);
  prefs.end();
  _logger.printf("OTA: Recorded failure %d/%d for v%s\n", count + 1, Ota::MaxRetries, version.c_str());
}

void ConfigStore::undoOtaFailure(const String& version) const
{
  Preferences prefs;
  prefs.begin(StorageKey::Namespace, false);
  if (prefs.getString(StorageKey::OtaFailVersion, "") == version)
  {
    const int count = prefs.getInt(StorageKey::OtaFailCount, 0);
    prefs.putInt(StorageKey::OtaFailCount, max(count - 1, 0));
  }
  prefs.end();
}

void ConfigStore::clearOtaFailCount() const
{
  Preferences prefs;
  prefs.begin(StorageKey::Namespace, false);
  prefs.remove(StorageKey::OtaFailVersion);
  prefs.remove(StorageKey::OtaFailCount);
  prefs.end();
}

String ConfigStore::getOtaPendingVersion() const
{
  Preferences prefs;
  prefs.begin(StorageKey::Namespace, true);
  String version = prefs.getString(StorageKey::OtaPendingVersion, "");
  prefs.end();
  return version;
}

int ConfigStore::incrementOtaPendingBoots() const
{
  Preferences prefs;
  prefs.begin(StorageKey::Namespace, false);
  const int boots = prefs.getInt(StorageKey::OtaPendingBoots, 0) + 1;
  prefs.putInt(StorageKey::OtaPendingBoots, boots);
  prefs.end();
  return boots;
}

void ConfigStore::setOtaPending(const String& version) const
{
  Preferences prefs;
  prefs.begin(StorageKey::Namespace, false);
  prefs.putString(StorageKey::OtaPendingVersion, version);
  prefs.putInt(StorageKey::OtaPendingBoots, 0);
  prefs.end();
}

void ConfigStore::clearOtaPending() const
{
  Preferences prefs;
  prefs.begin(StorageKey::Namespace, false);
  prefs.remove(StorageKey::OtaPendingVersion);
  prefs.remove(StorageKey::OtaPendingBoots);
  prefs.end();
}
