#pragma once

#include <Arduino.h>

struct DeviceConfig {
  String ssid;
  String password;
  String dashboardUrl;
  int devicePort;
  String dashboardBasePath;
  String dashboardApiKey;
  String pairingCode;
  String registrationToken;
  // Hex SHA-256 of the server certificate pinned on first connection; empty until then.
  String serverFingerprint;
};

struct ResponseHeaders {
  // 0 when the status line or the header block could not be read completely.
  int statusCode = 0;
  bool isSuccess = false;
  unsigned long retryAfterSeconds = 0;
  String firmwareVersion;
  String firmwareSha256;
  String firmwareSignature;
  long contentLength = -1;
};

struct DeviceStatus {
  bool success = false;
  uint64_t waitSeconds = 0;
  String latestFirmwareVersion;
};
