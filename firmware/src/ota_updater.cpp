#include "ota_updater.h"
#include "constants.h"
#include <Update.h>
#include <esp_ota_ops.h>

OtaUpdater::OtaUpdater(Logger& logger, Network& network, ConfigStore& configStore)
    : _logger(logger), _network(network), _configStore(configStore) {}

bool OtaUpdater::isNewerVersion(const char* current, const String& available) const
{
  int curMajor = 0, curMinor = 0, curPatch = 0;
  int avMajor = 0, avMinor = 0, avPatch = 0;

  sscanf(current, "%d.%d.%d", &curMajor, &curMinor, &curPatch);
  sscanf(available.c_str(), "%d.%d.%d", &avMajor, &avMinor, &avPatch);

  if (avMajor != curMajor) return avMajor > curMajor;
  if (avMinor != curMinor) return avMinor > curMinor;
  return avPatch > curPatch;
}

OtaFailure OtaUpdater::perform(DeviceConfig& config, const String& version)
{
  _logger.println("Starting OTA firmware update...");

  _network.setTimeout(30000);

  if (!_network.sendGetRequest("/api/firmware/download", config))
  {
    _logger.println("Failed to connect for OTA update");
    return OtaFailure::Transient;
  }

  auto headers = _network.readResponseHeaders();
  if (!headers.isSuccess)
  {
    _logger.println("OTA: Server returned an error");
    _network.close();
    return OtaFailure::Transient;
  }

  if (headers.contentLength <= 0)
  {
    _logger.println("OTA: Invalid or missing Content-Length header");
    _network.close();
    return OtaFailure::Transient;
  }

  // The release changed since the status check; the next wake picks up the new version.
  if (headers.firmwareVersion.length() > 0 && headers.firmwareVersion != version)
  {
    _logger.printf("OTA: Server now offers v%s instead of v%s\n",
                   headers.firmwareVersion.c_str(), version.c_str());
    _network.close();
    return OtaFailure::Transient;
  }

  _logger.printf("OTA: Firmware size: %ld bytes\n", headers.contentLength);

  // Counted up front so a crash or power loss while flashing still uses up an attempt.
  _configStore.recordOtaFailure(version);

  uint8_t expectedSha256[FirmwareSignature::Sha256Bytes];
  if (!FirmwareSignature::parseSha256Hex(headers.firmwareSha256, expectedSha256)
      || headers.firmwareSignature.length() == 0)
  {
    _logger.println("OTA: Server sent no SHA-256 or signature, refusing unsigned firmware");
    _network.close();
    return OtaFailure::Permanent;
  }

  if (!Update.begin(headers.contentLength))
  {
    _logger.println("OTA: Not enough space for update");
    _network.close();
    return OtaFailure::Permanent;
  }

  _logger.println("OTA: Writing firmware...");
  FirmwareSignature::ImageHasher hasher;
  const size_t written = streamImage(static_cast<size_t>(headers.contentLength), hasher);
  _logger.printf("OTA: Written %u bytes\n", written);
  _network.close();

  if (written != (size_t)headers.contentLength)
  {
    _logger.printf("OTA: Size mismatch — expected %ld, written %u\n", headers.contentLength, written);
    Update.abort();
    _configStore.undoOtaFailure(version);
    return OtaFailure::Transient;
  }

  // Nothing is marked bootable until both checks pass; Update.abort() keeps the running firmware.
  uint8_t actualSha256[FirmwareSignature::Sha256Bytes];
  if (!hasher.finish(actualSha256)
      || memcmp(actualSha256, expectedSha256, sizeof(actualSha256)) != 0)
  {
    _logger.println("OTA: SHA-256 mismatch, discarding image");
    Update.abort();
    return OtaFailure::Permanent;
  }

  String signatureError;
  if (!FirmwareSignature::verify(version, actualSha256, headers.firmwareSignature, signatureError))
  {
    _logger.printf("OTA: Signature check failed (%s), discarding image\n", signatureError.c_str());
    Update.abort();
    return OtaFailure::Permanent;
  }
  _logger.println("OTA: Signature verified");

  if (Update.end() && Update.isFinished())
  {
    _logger.println("OTA: Update successful! Rebooting...");
    _configStore.setOtaPending(version);
    delay(1000);
    ESP.restart();
  }

  _logger.printf("OTA: Update failed: %s\n", Update.errorString());
  return OtaFailure::Permanent;
}

size_t OtaUpdater::streamImage(size_t length, FirmwareSignature::ImageHasher& hasher)
{
  constexpr unsigned long StallTimeoutMs = 30000;
  uint8_t buffer[1024];
  size_t written = 0;
  unsigned long lastDataAt = millis();

  while (written < length)
  {
    const int available = _network.available();
    if (available <= 0)
    {
      if (!_network.connected() || millis() - lastDataAt > StallTimeoutMs) break;
      delay(1);
      continue;
    }

    const size_t toRead = min(min(static_cast<size_t>(available), sizeof(buffer)), length - written);
    const size_t bytesRead = _network.read(buffer, toRead);
    if (bytesRead == 0) continue;

    lastDataAt = millis();
    hasher.update(buffer, bytesRead);
    if (Update.write(buffer, bytesRead) != bytesRead) break;
    written += bytesRead;
  }
  return written;
}

void OtaUpdater::verifyPendingUpdate()
{
  // Only covers failures after this point; a firmware that crashes before setup()
  // gets here cannot be recovered without bootloader rollback support.
  const String pendingVersion = _configStore.getOtaPendingVersion();
  if (pendingVersion.length() == 0)
  {
    return;
  }

  const int boots = _configStore.incrementOtaPendingBoots();
  _logger.printf("OTA: v%s awaiting verification (boot %d/%d)\n",
                 pendingVersion.c_str(), boots, Ota::MaxVerifyBoots);
  if (boots <= Ota::MaxVerifyBoots)
  {
    return;
  }

  _configStore.clearOtaPending();
  const esp_partition_t* previous = esp_ota_get_next_update_partition(nullptr);
  if (previous == nullptr || esp_ota_set_boot_partition(previous) != ESP_OK)
  {
    _logger.println("OTA: No previous firmware to restore");
    return;
  }

  // The failure recorded before flashing stays, so the version is retried at most Ota::MaxRetries times.
  _logger.println("OTA: New firmware never reached the dashboard, restoring previous firmware...");
  delay(1000);
  ESP.restart();
}

void OtaUpdater::confirmUpdate()
{
  if (_configStore.getOtaPendingVersion().length() == 0)
  {
    return;
  }

  _logger.println("OTA: New firmware verified");
  _configStore.clearOtaPending();
  _configStore.clearOtaFailCount();
}
