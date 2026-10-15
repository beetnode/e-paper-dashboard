#include <Arduino.h>
#include <esp_task_wdt.h>
#include "version.h"
#include "constants.h"
#include "logger.h"
#include "config_store.h"
#include "display_manager.h"
#include "network.h"
#include "ota_updater.h"
#include "device_api.h"
#include "setup_portal.h"
#include "hardware.h"

static Logger logger;
static ConfigStore configStore(logger);
static DisplayManager displayManager(logger);
static Network network(logger);
static OtaUpdater otaUpdater(logger, network, configStore);
static DeviceApi deviceApi(logger, network);
static Hardware hardware;

void setup()
{
  logger.begin(115200);

  if (!displayManager.init())
  {
    logger.println("Failed to initialize display!");
    ESP.restart();
  }

  logger.print("izBoard Firmware v");
  logger.println(FIRMWARE_VERSION);

  otaUpdater.verifyPendingUpdate();

  if (hardware.isResetRequested())
  {
    logger.println("Reset requested, clearing configuration...");
    hardware.resetDevice(configStore);
    return;
  }

  auto configuration = configStore.load();
  DeviceConfig config;
  if (!configuration.has_value())
  {
    SetupPortal portal(logger, configStore, displayManager, network, deviceApi);
    config = portal.run();
  }
  else
  {
    config = configuration.value();
  }

  if (WiFi.status() != WL_CONNECTED && !network.connectToWiFi(config.ssid, config.password))
  {
    if (config.dashboardApiKey.length() == 0)
    {
      logger.println("Pending pairing could not reach WiFi; reopening setup...");
      configStore.clear();
      SetupPortal portal(logger, configStore, displayManager, network, deviceApi);
      config = portal.run();
    }
    else
    {
      logger.println("WiFi connection failed, retrying after sleep");
      displayManager.powerOff();
      hardware.sleepAfterFailure();
      return;
    }
  }

  if (config.dashboardApiKey.length() == 0)
  {
    if (config.pairingCode.length() == 0 || config.registrationToken.length() == 0)
    {
      logger.println("Incomplete pairing state, restarting setup...");
      hardware.resetDevice(configStore);
      return;
    }

    displayManager.showSuccess(
        "Ready to Claim",
        "Open the dashboard on your network.\nEnter this claim code:",
        config.pairingCode);
    String apiKey;
    String pairingError;
    auto pairingResult = deviceApi.registerWithDashboard(
        config.pairingCode, config.registrationToken,
        config.dashboardUrl, config.devicePort, config.useHttps,
        config.dashboardBasePath, apiKey, pairingError);
    if (pairingResult != PairingAttemptResult::Success)
    {
      const bool retryable = pairingResult == PairingAttemptResult::RetryableFailure;
      logger.println(retryable
          ? "Pairing service unavailable; preserving setup for retry..."
          : "Pairing could not be resumed; restarting setup...");
      if (!retryable)
      {
        configStore.clear();
      }
      displayManager.showSuccess(
          retryable ? "Pairing Paused" : "Pairing Failed",
          pairingError,
          retryable ? "Retrying automatically..." : "Restarting setup...");
      if (retryable)
      {
        displayManager.powerOff();
        hardware.sleepAfterFailure();
        return;
      }
      delay(5000);
      ESP.restart();
      return;
    }

    config.dashboardApiKey = apiKey;
    config.pairingCode = "";
    config.registrationToken = "";
    configStore.save(config);
    displayManager.showSuccess(
        "Paired Successfully",
        "The device will fetch and display\nassigned dashboards automatically.",
        "Press the button to refresh manually.");
  }

  // Reboots the device if anything below hangs instead of reaching deep sleep.
  esp_task_wdt_init(Retry::WatchdogTimeoutSeconds, true);
  esp_task_wdt_add(nullptr);

  uint64_t waitSeconds = Timing::FallbackRefreshSeconds;

  auto status = deviceApi.fetchDeviceStatus(config);
  if (!status.success)
  {
    logger.println("Dashboard unavailable, retrying after sleep");
    displayManager.powerOff();
    hardware.sleepAfterFailure();
    return;
  }
  otaUpdater.confirmUpdate();

  if (status.waitSeconds > 0)
  {
    waitSeconds = status.waitSeconds;
  }

  if (status.latestFirmwareVersion.length() > 0 &&
      otaUpdater.isNewerVersion(FIRMWARE_VERSION, status.latestFirmwareVersion))
  {
    int failCount = configStore.getOtaFailCount(status.latestFirmwareVersion);
    if (failCount >= Ota::MaxRetries)
    {
      logger.printf("OTA: Skipping v%s — failed %d times already\n",
                    status.latestFirmwareVersion.c_str(), failCount);
    }
    else
    {
      logger.print("New firmware v");
      logger.print(status.latestFirmwareVersion);
      logger.printf(" available (attempt %d/%d). Starting OTA update...\n", failCount + 1, Ota::MaxRetries);
      displayManager.showSuccess(
          "Updating Firmware",
          "v" + String(FIRMWARE_VERSION) + " -> v" + status.latestFirmwareVersion,
          "Do not power off the device.");
      // Downloading and flashing can take longer than the watchdog timeout.
      esp_task_wdt_delete(nullptr);
      auto failure = otaUpdater.perform(config, status.latestFirmwareVersion);
      esp_task_wdt_add(nullptr);
      logger.println(failure == OtaFailure::Transient
          ? "OTA update interrupted, will retry on next wake"
          : "OTA update failed, continuing with current firmware");
    }
  }

  if (!deviceApi.fetchAndDisplayImage(config, displayManager))
  {
    logger.println("Image unavailable, retrying after sleep");
    displayManager.powerOff();
    hardware.sleepAfterFailure();
    return;
  }

  displayManager.refresh();
  displayManager.powerOff();
  hardware.resetFailureBackoff();
  hardware.startDeepSleep(waitSeconds);
}

void loop()
{
  ESP.restart();
}
