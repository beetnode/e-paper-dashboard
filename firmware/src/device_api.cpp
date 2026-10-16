#include "device_api.h"
#include "constants.h"
#include <ArduinoJson.h>

namespace {
constexpr size_t MaxPairingResponseBytes = 4096;
constexpr unsigned long DefaultClaimExpirySeconds = 10UL * 60UL;
constexpr const char* IdentityChangedMessage = "Server certificate changed";

String pairingErrorMessage(const String& response, const String& fallback)
{
  JsonDocument errorDoc;
  if (!deserializeJson(errorDoc, response))
  {
    const char* detail = errorDoc["detail"];
    if (detail && strlen(detail) > 0) return String(detail);
  }
  return response.length() > 0 && response.length() <= 120 ? response : fallback;
}

bool isRetryableHttpStatus(int status)
{
  return status == 408 || status == 425 || status == 429 || status >= 500;
}

String hostHeader(const String& host, int port)
{
  const String formattedHost = host.indexOf(':') >= 0 ? "[" + host + "]" : host;
  return formattedHost + ":" + String(port);
}
}

DeviceApi::DeviceApi(Logger& logger, Network& network) : _logger(logger), _network(network) {}

bool DeviceApi::openSuccessfulResponse(const String& path, DeviceConfig& config,
                                       ResponseHeaders& headers)
{
  for (int attempt = 1; attempt <= Retry::RequestAttempts; ++attempt)
  {
    if (attempt > 1)
    {
      _logger.printf("Retrying %s (attempt %d/%d)...\n", path.c_str(), attempt, Retry::RequestAttempts);
      delay(Retry::RequestRetryDelayMs);
    }

    if (!_network.sendGetRequest(path, config))
    {
      continue;
    }

    headers = _network.readResponseHeaders();
    if (headers.isSuccess)
    {
      return true;
    }

    _logger.println("The request was not successful");
    _network.close();
  }
  return false;
}

DeviceStatus DeviceApi::fetchDeviceStatus(DeviceConfig& config)
{
  _logger.println("Fetching device configuration...");

  DeviceStatus result;

  ResponseHeaders headers;
  if (!openSuccessfulResponse("/api/configuration/next-update-wait-seconds", config, headers))
  {
    _logger.println("Failed to fetch device config");
    return result;
  }

  result.success = true;
  result.latestFirmwareVersion = headers.firmwareVersion;

  String delayString{};
  if (_network.connected() || _network.available())
  {
    delayString = _network.readStringUntil('\n');
    _logger.print("Next update wait: ");
    _logger.println(delayString);
  }

  _network.close();

  if (delayString.length() > 0)
  {
    result.waitSeconds = strtoull(delayString.c_str(), nullptr, 10);
  }

  if (result.latestFirmwareVersion.length() > 0)
  {
    _logger.print("Latest firmware version from server: ");
    _logger.println(result.latestFirmwareVersion);
  }

  return result;
}

bool DeviceApi::fetchAndDisplayImage(DeviceConfig& config, DisplayManager& display)
{
  _logger.println("Connecting to the remote server...");

  _network.setTimeout(5000);
  ResponseHeaders headers;
  if (!openSuccessfulResponse("/api/render/binary", config, headers))
  {
    _logger.println("Failed to fetch the image");
    return false;
  }

  _logger.println("Reading image content...");

  display.beginPartialWindow();

  int16_t y = 0;

  while ((_network.connected() || _network.available()) && y < DisplayConst::Height)
  {
    const size_t bytesNeeded = static_cast<size_t>(DisplayConst::FrameBytes) * 2;
    size_t idx = 0;

    display.clearBuffers();

    while (idx < bytesNeeded && (_network.connected() || _network.available()))
    {
      size_t avail = _network.available();
      if (avail > 0)
      {
        size_t toRead = min(avail, bytesNeeded - idx);
        uint8_t buffer[1024];
        size_t chunkSize = min(toRead, sizeof(buffer));
        size_t bytesRead = _network.read(buffer, chunkSize);

        for (size_t i = 0; i < bytesRead; i++)
        {
          display.writePixelByte(idx, buffer[i]);
          ++idx;
        }
      }
      else
      {
        if (!_network.connected())
        {
          break;
        }
        yield();
      }
    }

    if (idx < bytesNeeded)
    {
      _logger.println("Incomplete frame data received, stopping.");
      break;
    }

    display.writeFrame(0, y, DisplayConst::FrameWidth, DisplayConst::FrameHeight);
    y += DisplayConst::FrameHeight;
  }
  _logger.println();

  _network.close();
  return y >= DisplayConst::Height;
}

bool DeviceApi::postPairingRequest(const String& path, const String& body,
                                   const String& dashboardUrl, int devicePort, String& serverFingerprint,
                                   const String& dashboardBasePath,
                                   int& httpStatus, unsigned long& retryAfterSeconds, String& response)
{
  if (!_network.connectTo(dashboardUrl, devicePort, serverFingerprint))
  {
    return false;
  }
  _network.setTimeout(10000);

  String postRequest = "POST " + dashboardBasePath + path + " HTTP/1.1\r\n";
  postRequest += "Host: " + hostHeader(dashboardUrl, devicePort) + "\r\n";
  postRequest += "Content-Type: application/json\r\n";
  postRequest += "Content-Length: " + String(body.length()) + "\r\n";
  postRequest += "Connection: close\r\n\r\n";
  postRequest += body;

  _network.send(postRequest);

  httpStatus = 0;
  retryAfterSeconds = 0;
  while (_network.connected() || _network.available())
  {
    String line = _network.readStringUntil('\n');
    if (httpStatus == 0 && line.startsWith("HTTP/"))
    {
      int spaceIdx = line.indexOf(' ');
      if (spaceIdx > 0)
      {
        httpStatus = line.substring(spaceIdx + 1).toInt();
      }
    }

    if (line.startsWith("Retry-After:"))
    {
      retryAfterSeconds = line.substring(strlen("Retry-After:")).toInt();
    }

    if (line == "\r")
    {
      break;
    }
  }

  String raw = "";
  while (_network.connected() || _network.available())
  {
    if (_network.available())
    {
      if (raw.length() >= MaxPairingResponseBytes)
      {
        _network.close();
        response = "Server response was too large";
        return false;
      }
      raw += (char)_network.client().read();
    }
    else
    {
      delay(5);
    }
  }

  _network.close();

  int jsonStart = raw.indexOf('{');
  int jsonEnd = raw.lastIndexOf('}');
  response = (jsonStart >= 0 && jsonEnd > jsonStart)
                 ? raw.substring(jsonStart, jsonEnd + 1)
                 : raw;
  response.trim();

  _logger.print("HTTP status: ");
  _logger.println(httpStatus);
  return true;
}

PairingAttemptResult DeviceApi::announceDevice(
    const String& pairingCode, const String& registrationToken,
    const String& dashboardUrl, int devicePort, String& serverFingerprint,
    const String& dashboardBasePath, unsigned long& expiresInSeconds, String& errorOut)
{
  _logger.println("Announcing device to dashboard...");

  String macAddress = WiFi.macAddress();
  String deviceName = "izBoard-" + macAddress.substring(macAddress.length() - 8);

  JsonDocument announceDoc;
  announceDoc["code"] = pairingCode;
  announceDoc["registrationToken"] = registrationToken;
  announceDoc["deviceIdentifier"] = macAddress;
  announceDoc["deviceName"] = deviceName;
  announceDoc["screenWidth"] = DisplayConst::Width;
  announceDoc["screenHeight"] = DisplayConst::Height;

  String announceBody;
  serializeJson(announceDoc, announceBody);

  int httpStatus = 0;
  unsigned long retryAfterSeconds = 0;
  String response;
  if (!postPairingRequest("/api/pairing/announce", announceBody,
                          dashboardUrl, devicePort, serverFingerprint, dashboardBasePath,
                          httpStatus, retryAfterSeconds, response))
  {
    if (_network.identityMismatch())
    {
      errorOut = IdentityChangedMessage;
      return PairingAttemptResult::TerminalFailure;
    }
    errorOut = "Could not connect to server";
    return PairingAttemptResult::RetryableFailure;
  }

  if (httpStatus != 202)
  {
    errorOut = pairingErrorMessage(response, "Server rejected device announcement");
    return isRetryableHttpStatus(httpStatus)
        ? PairingAttemptResult::RetryableFailure
        : PairingAttemptResult::TerminalFailure;
  }

  JsonDocument responseDoc;
  expiresInSeconds = DefaultClaimExpirySeconds;
  if (!deserializeJson(responseDoc, response))
  {
    expiresInSeconds = responseDoc["expiresInSeconds"] | DefaultClaimExpirySeconds;
  }

  return PairingAttemptResult::Success;
}

PairingAttemptResult DeviceApi::waitForClaim(
    const String& pairingCode, const String& registrationToken,
    const String& dashboardUrl, int devicePort, String& serverFingerprint,
    const String& dashboardBasePath, unsigned long expiresInSeconds,
    String& apiKey, String& errorOut)
{

  _logger.println("Device announced. Waiting for claim in the dashboard...");
  unsigned long deadline = millis() + (expiresInSeconds + 5UL) * 1000UL;
  bool receivedServerStatus = false;

  JsonDocument statusRequestDoc;
  statusRequestDoc["code"] = pairingCode;
  statusRequestDoc["registrationToken"] = registrationToken;
  String statusBody;
  serializeJson(statusRequestDoc, statusBody);
  String response;

  while (static_cast<long>(deadline - millis()) > 0)
  {
    delay(2000);
    response = "";
    int httpStatus = 0;
    unsigned long retryAfterSeconds = 0;
    if (!postPairingRequest("/api/pairing/device-status", statusBody,
                            dashboardUrl, devicePort, serverFingerprint, dashboardBasePath,
                            httpStatus, retryAfterSeconds, response))
    {
      if (_network.identityMismatch())
      {
        errorOut = IdentityChangedMessage;
        return PairingAttemptResult::TerminalFailure;
      }
      _logger.println("Pairing status temporarily unavailable; retrying...");
      continue;
    }

    if (httpStatus == 410)
    {
      errorOut = "Claim code expired";
      return PairingAttemptResult::Expired;
    }
    if (httpStatus != 200)
    {
      if (isRetryableHttpStatus(httpStatus))
      {
        const unsigned long retryDelay = constrain(retryAfterSeconds, 1UL, 30UL);
        _logger.println("Pairing status temporarily rejected; retrying...");
        delay(retryDelay * 1000UL);
        continue;
      }
      errorOut = pairingErrorMessage(response, "Server rejected pairing status request");
      return PairingAttemptResult::TerminalFailure;
    }

    JsonDocument responseDoc;
    if (deserializeJson(responseDoc, response))
    {
      _logger.println("Invalid pairing status response; retrying...");
      continue;
    }

    const char* status = responseDoc["status"] | "pending";
    receivedServerStatus = true;
    const unsigned long remaining = responseDoc["expiresInSeconds"] | 0UL;
    if (remaining > 0)
    {
      deadline = millis() + (remaining + 5UL) * 1000UL;
    }
    if (strcmp(status, "completed") == 0)
    {
      const char* key = responseDoc["apiKey"];
      if (!key || strlen(key) == 0)
      {
        errorOut = "Server response missing API key";
        return PairingAttemptResult::TerminalFailure;
      }

      apiKey = key;
      _logger.println("Device claim completed");
      return PairingAttemptResult::Success;
    }
  }

  errorOut = receivedServerStatus
      ? "Claim code expired before it was entered"
      : "Server stayed unavailable during pairing";
  return receivedServerStatus ? PairingAttemptResult::Expired : PairingAttemptResult::RetryableFailure;
}

PairingAttemptResult DeviceApi::registerWithDashboard(
    const String& pairingCode, const String& registrationToken,
    const String& dashboardUrl, int devicePort, String& serverFingerprint,
    const String& dashboardBasePath, String& apiKey, String& errorOut)
{
  unsigned long expiresInSeconds = DefaultClaimExpirySeconds;
  auto result = announceDevice(pairingCode, registrationToken, dashboardUrl, devicePort,
                               serverFingerprint, dashboardBasePath, expiresInSeconds, errorOut);
  if (result != PairingAttemptResult::Success)
  {
    return result;
  }

  return waitForClaim(pairingCode, registrationToken, dashboardUrl, devicePort,
                      serverFingerprint, dashboardBasePath, expiresInSeconds, apiKey, errorOut);
}
