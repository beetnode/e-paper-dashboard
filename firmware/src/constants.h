#pragma once

#include <Arduino.h>

namespace Pin {
  constexpr gpio_num_t ResetWakeup = GPIO_NUM_33;
  constexpr uint8_t Led = 2;
  constexpr int8_t DisplayCs = 15;
  constexpr int8_t DisplayDc = 27;
  constexpr int8_t DisplayRst = 26;
  constexpr int8_t DisplayBusy = 25;
  constexpr int8_t SpiSck = 13;
  constexpr int8_t SpiMiso = 12;
  constexpr int8_t SpiMosi = 14;
  constexpr int8_t SpiSs = 15;
}

namespace Timing {
  constexpr uint64_t SecToUsec = 1000000;
  constexpr uint64_t FallbackRefreshSeconds = 4 * 3600;
  constexpr unsigned long ResetRequestTimeoutSec = 10;
  constexpr unsigned long BlinkIntervalMs = 500;
}

namespace DisplayConst {
  constexpr uint16_t Width = 800;
  constexpr uint16_t Height = 480;
  constexpr uint16_t FrameWidth = Width;
  constexpr uint16_t FrameHeight = 160;
  constexpr uint16_t FrameBytes = FrameWidth * FrameHeight / 8;
}

namespace Retry {
  constexpr int RequestAttempts = 3;
  constexpr unsigned long RequestRetryDelayMs = 3000;
  constexpr uint64_t BackoffBaseSeconds = 60;
  constexpr uint64_t BackoffMaxSeconds = 30 * 60;
  constexpr uint32_t WatchdogTimeoutSeconds = 120;
}

namespace Ota {
  constexpr int MaxRetries = 3;
  // Boots (including deep-sleep wakes) a new firmware gets to reach the dashboard
  // before the previous firmware is restored.
  constexpr int MaxVerifyBoots = 10;
}

namespace StorageKey {
  constexpr const char* Namespace = "config";
  constexpr const char* Ssid = "ssid";
  constexpr const char* Password = "pwd";
  constexpr const char* DashboardUrl = "url";
  constexpr const char* DevicePort = "devport";
  constexpr const char* DashboardBasePath = "basepath";
  constexpr const char* DashboardApiKey = "apikey";
  constexpr const char* PairingCode = "paircode";
  constexpr const char* RegistrationToken = "regtoken";
  constexpr const char* UseHttps = "usehttps";
  constexpr const char* OtaFailVersion = "otafailv";
  constexpr const char* OtaFailCount = "otafailc";
  constexpr const char* OtaPendingVersion = "otapendv";
  constexpr const char* OtaPendingBoots = "otapendb";
}
