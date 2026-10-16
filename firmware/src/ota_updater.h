#pragma once

#include "types.h"
#include "config_store.h"
#include "network.h"
#include "logger.h"
#include "firmware_signature.h"

enum class OtaFailure {
  // Network problem; the attempt does not count towards Ota::MaxRetries.
  Transient,
  // The image could not be written or verified; the attempt counts.
  Permanent
};

class OtaUpdater {
public:
  OtaUpdater(Logger& logger, Network& network, ConfigStore& configStore);

  bool isNewerVersion(const char* current, const String& available) const;
  // Downloads and flashes the firmware, rebooting on success. Returns only on failure.
  // The image is installed only when its SHA-256 matches and its release signature verifies.
  OtaFailure perform(const DeviceConfig& config, const String& version);
  // Call early in setup(): restores the previous firmware when a freshly installed
  // one has booted Ota::MaxVerifyBoots times without reaching the dashboard.
  void verifyPendingUpdate();
  // Call once the dashboard was reached: the running firmware is considered good.
  void confirmUpdate();

private:
  Logger& _logger;
  Network& _network;
  ConfigStore& _configStore;
  // Writes up to length bytes of the response body to the update partition, hashing them.
  size_t streamImage(size_t length, FirmwareSignature::ImageHasher& hasher);
};
