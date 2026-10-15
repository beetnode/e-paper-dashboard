#pragma once

#include "types.h"
#include "network.h"
#include "logger.h"
#include "display_manager.h"

enum class PairingAttemptResult {
  Success,
  RetryableFailure,
  TerminalFailure,
  Expired
};

class DeviceApi {
public:
  DeviceApi(Logger& logger, Network& network);

  DeviceStatus fetchDeviceStatus(const DeviceConfig& config);
  bool fetchAndDisplayImage(const DeviceConfig& config, DisplayManager& display);
  PairingAttemptResult announceDevice(const String& pairingCode, const String& registrationToken,
                                      const String& dashboardUrl, int devicePort, bool useHttps,
                                      const String& dashboardBasePath,
                                      unsigned long& expiresInSeconds, String& errorOut);
  PairingAttemptResult waitForClaim(const String& pairingCode, const String& registrationToken,
                                    const String& dashboardUrl, int devicePort, bool useHttps,
                                    const String& dashboardBasePath, unsigned long expiresInSeconds,
                                    String& apiKey, String& errorOut);
  PairingAttemptResult registerWithDashboard(const String& pairingCode, const String& registrationToken,
                                             const String& dashboardUrl, int devicePort, bool useHttps,
                                             const String& dashboardBasePath,
                                             String& apiKey, String& errorOut);

private:
  Logger& _logger;
  Network& _network;
  // Sends a GET request, retrying connection failures and non-200 responses.
  // On success the response body is ready to be read from the network.
  bool openSuccessfulResponse(const String& path, const DeviceConfig& config, ResponseHeaders& headers);
  bool postPairingRequest(const String& path, const String& body,
                          const String& dashboardUrl, int devicePort, bool useHttps,
                          const String& dashboardBasePath,
                          int& httpStatus, unsigned long& retryAfterSeconds, String& response);
};
